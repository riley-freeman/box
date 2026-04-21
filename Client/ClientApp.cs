using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Crayon.Box.Messages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.ObjectPool;

namespace Crayon.Box;

public class ClientApp : IDisposable
{
    private readonly SqliteConnection _sqlConn;

    private const int MaxConnectionRetryTimeout = 60;
    private const int ConnectionRetryTimeoutStep = 5;

    private ClientWebSocket? _cws = null;
    private ServerHandle? _server = null;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed = false;

    public ClientApp()
    {
        // Connect to the User's database
        _sqlConn = new SqliteConnection($"Data Source={DefaultDataPath() + "/box.db"}");
        _sqlConn.Open();

        using var createResourcesCommand = _sqlConn.CreateCommand();
        createResourcesCommand.CommandText = """
        CREATE TABLE IF NOT EXISTS resources (
            original_name VARCHAR(255)  NOT NULL,
            nickname      VARCHAR(100)  UNIQUE,
            uuid          BINARY(16)    NOT NULL,

            UNIQUE (uuid)
        );
        CREATE TABLE IF NOT EXISTS user_table (
            username        VARCHAR(32)     NOT NULL UNIQUE,
            display_name    VARCHAR(32)     NOT NULL UNIQUE,
            session_id      VARCHAR(36)     NOT NULL UNIQUE
        );
        """;
        createResourcesCommand.ExecuteReader();


    }

    public async Task ImportFile(string path, string? nickname)
    {
        string nick = Path.GetFileName(path);
        if (nickname != null) nick = nickname;

        var uuid = Guid.NewGuid();
        File.Copy(path, DefaultResourcesPath() + "/" + uuid);

        try
        {
            using var cmd = _sqlConn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO resources (original_name, nickname, uuid)
                VALUES (@originalName, @nickname, @uuid)";

            cmd.Parameters.AddWithValue("@originalName", Path.GetFileName(path));
            cmd.Parameters.AddWithValue("@nickname", nick);
            cmd.Parameters.AddWithValue("@uuid", uuid.ToByteArray());

            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) // UNIQUE constraint failed
        {
            Console.WriteLine($"Failed to import file; it's already imported: {nick}");
        }
    }

    private string? LookupFile(string name)
    {
        using var getCmd = _sqlConn.CreateCommand();
        getCmd.CommandText = @"SELECT uuid FROM resources WHERE nickname = @name;";
        getCmd.Parameters.AddWithValue("@name", name);

        using var reader = getCmd.ExecuteReader();
        while (reader.Read())
        {
            byte[] buffer = new byte[16];
            reader.GetBytes(0, 0, buffer, 0, buffer.Length);

            Guid uuid = new(buffer);
            if (File.Exists(DefaultResourcesPath() + "/" + uuid))
                return DefaultResourcesPath() + "/" + uuid;
        }

        return null;

    }

    public async Task SetDisplayName(string displayName)
    {
        using var cmd = _sqlConn.CreateCommand();
        cmd.CommandText = "UPDATE user_table SET display_name = @DisplayName;";
        cmd.Parameters.AddWithValue("@DisplayName", displayName);
        int rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            Console.WriteLine("No active session — connect first.");
        else
            Console.WriteLine($"Display name updated to '{displayName}'.");
    }

    public async Task RemoveFile(string name)
    {

        var path = LookupFile(name);
        if (path != null)
            File.Delete(path);

        using var removeCmd = _sqlConn.CreateCommand();
        removeCmd.CommandText = @"DELETE FROM resources WHERE nickname = @name;";
        removeCmd.Parameters.AddWithValue("@name", name);

        await removeCmd.ExecuteNonQueryAsync();
    }

    public void Serve(Uri uri, string? username, string? password)
    {
        _cws = ConnectSocket(uri, MaxConnectionRetryTimeout, ConnectionRetryTimeoutStep);
        _server = new ServerHandle(_cws);

        Task.Run(async () =>
        {
            var ct = _cts.Token;

            // Validate the server signature
            var ssBuffer = new byte[Config.ServerSignature.Length];
            await _cws.ReceiveAsync(ssBuffer.AsMemory(), ct);
            var serverSignature = Config.StringEncoding.GetString(ssBuffer);

            if (serverSignature != Config.ServerSignature)
                throw new InvalidCredentialException("Server's signature is wrong.");

            // Get the server's info
            Message serverInfoMsg = (await _server.ReceiveMessageAsync())!;
            if (serverInfoMsg.Type != MessageType.ServerInfo)
                throw new InvalidCredentialException("Expected to receive server information.");

            // Send over our client's signature
            var signatureBytes = Config.StringEncoding.GetBytes(Config.ClientSignature);
            var signatureSegment = new ArraySegment<byte>(signatureBytes);
            await _cws.SendAsync(signatureSegment, WebSocketMessageType.Text, true, CancellationToken.None);

            // Send over our client's info
            var clientVersion = Assembly.GetEntryAssembly()!.GetName().Version!.ToString();
            var clientInfo = new ClientInformation(clientVersion, Config.Organization);
            var clientInfoEncoded = Config.StringEncoding.GetBytes(JsonSerializer.Serialize(clientInfo));
            var clientInfoMessage = new Message(MessageType.ClientInfo, clientInfoEncoded);
            await _server.SendMessageAsync(clientInfoMessage, null);

            // Get a session ID & update the client's privileges.
            var sessionId = await GetSessionIDAsync(username, password);
            var auth = new Authorization(sessionId);

            var authBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(auth));
            await new Message(MessageType.PrivilegeElevation, authBytes).SendAsync(_cws);

            await EnterMessageLoop(ct);
        }).Wait();
    }

    public async Task<Guid> GetSessionIDAsync(string? username, string? password)
    {
        try
        {
            using var getUser = _sqlConn.CreateCommand();
            getUser.CommandText = "SELECT username, session_id FROM user_table;";
            await using var r = await getUser.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                throw new SqliteException("No cached session.", 0);
            return Guid.Parse(r.GetString(1));
        }
        catch (Exception)
        {
            var u = username ?? ReadUsername();
            var p = password ?? ReadPassword();
            using var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{Config.ServerAddress}:{Config.ServerPort}");

            bool questioning = true;
            LoginResponse? body = null;
            while (questioning)
            {
                var formData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", u),
                    new KeyValuePair<string, string>("password", p),
                });
                var response = await client.PostAsync("/api/user/login-session", formData);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    body = await response.Content.ReadFromJsonAsync<LoginResponse>(CancellationToken.None);
                    break;
                }
                else
                {
                    Console.WriteLine("Bad Username or Password, please retry!");
                    u = username ?? ReadUsername();
                    p = password ?? ReadPassword();
                }
            }

            using var command0 = _sqlConn.CreateCommand();
            command0.CommandText = """
                DELETE FROM user_table;
                INSERT INTO user_table (username, display_name, session_id) VALUES (@Username, @DisplayName, @SessionID);
            """;
            command0.Parameters.AddWithValue("@Username", u);
            command0.Parameters.AddWithValue("@DisplayName", u);
            command0.Parameters.AddWithValue("@SessionID", body!.Value.SessionID().ToString());
            await command0.ExecuteNonQueryAsync();

            return body!.Value.SessionID();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        if (_cws != null)
        {
            if (_cws.State == WebSocketState.Open)
                Message.SendCloseAsync(_cws, WebSocketCloseStatus.NormalClosure, string.Empty).Wait();
            _cws.Dispose();
            _cws = null;
        }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    ~ClientApp()
    {
        if (!_disposed) Dispose();
    }

    private async Task EnterMessageLoop(CancellationToken ct)
    {
        while (_cws!.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            Message? message;
            try
            {
                message = await _server!.ReceiveMessageAsync();
                if (message == null) continue;
                switch (message.Type)
                {
                    case MessageType.AuthSuccessful:
                        Console.WriteLine("Successfully logged in to the network!");
                        break;

                    case MessageType.AuthFailed:
                        {
                            Console.WriteLine("Authentication has either failed or been revoked! Please sign in!");

                            // Remove the cached session ID, if it exists
                            using var command0 = _sqlConn.CreateCommand();
                            command0.CommandText = "DELETE FROM user_table;";
                            await command0.ExecuteNonQueryAsync();

                            // Get a new session ID & update the client's privileges.
                            var sessionID = await GetSessionIDAsync(null, null);
                            var auth = new Authorization(sessionID);
                            var authBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(auth));
                            await new Message(MessageType.PrivilegeElevation, authBytes).SendAsync(_cws);
                            break;
                        }


                    case MessageType.GetWasmPage:
                        await SendFileBack($"{DefaultDataPath()}/user_theme.box/binaries.wasm", message, MessageType.WasmPageResponse);
                        break;

                    case MessageType.GetResource:
                        {
                            var jsonReqData = Encoding.UTF8.GetString(message.RawData.ToArray());
                            var reqData = JsonSerializer.Deserialize<RequestResourceInformation>(jsonReqData)!;

                            var path = LookupFile(reqData.ResourceName)!;
                            if (path == null)
                            {
                                var errRes = new Message(message, MessageType.ResourceResponse, HttpStatusCode.NotFound, Array.Empty<byte>());
                                await _server.SendMessageAsync(errRes, null);
                                break;
                            }
                            await SendFileBack(path, message, MessageType.ResourceResponse);
                            break;
                        }

                    case MessageType.GetDisplayName:
                        {
                            using var cmd = _sqlConn.CreateCommand();
                            cmd.CommandText = "SELECT display_name FROM user_table LIMIT 1;";
                            await using var r = await cmd.ExecuteReaderAsync(ct);
                            var displayName = await r.ReadAsync(ct) ? r.GetString(0) : string.Empty;
                            var nameBytes = Encoding.UTF8.GetBytes(displayName);
                            var reply = new Message(message, MessageType.DisplayNameResponse, HttpStatusCode.OK, nameBytes);
                            await _server!.SendMessageAsync(reply, null);
                            break;
                        }

                    case MessageType.CloseRequest:
                        {
                            var jsonReqData = Encoding.UTF8.GetString(message.RawData.ToArray());
                            var reqData = JsonSerializer.Deserialize<CloseInformation>(jsonReqData);
                            await Message.SendCloseOutputAsync(_cws, reqData.CloseStatus, reqData.Description);
                            return;
                        }

                    case MessageType.Invalid:
                    default:
                        Console.Error.WriteLine($"Received unknown message type: {message.Type}");
                        continue;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException e) when (
                e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely ||
                e.WebSocketErrorCode == WebSocketError.Faulted)
            {
                break;
            }
            catch (WebSocketException e)
            {
                Console.Error.WriteLine($"WebSocket error: {e.WebSocketErrorCode} — {e.Message}");
                break;
            }
        }
    }

    private async Task SendFileBack(string path, Message originalMessage, MessageType messageType)
    {
        if (!File.Exists(path))
        {
            var errRes = new Message(originalMessage, messageType, HttpStatusCode.NotFound, Array.Empty<byte>());
            await _server!.SendMessageAsync(errRes, null);
            return;
        }
        var data = await File.ReadAllBytesAsync(path);
        var response = new Message(originalMessage, messageType, HttpStatusCode.OK, data);
        await response.SendAsync(_cws!);
    }

    private static ClientWebSocket ConnectSocket(Uri uri, int maxTimeout, int step)
    {
        var timeout = step;
        ClientWebSocket? socket = null;
        while (true)
            try
            {
                Console.WriteLine($"Connecting to '{uri}'");
                socket = new ClientWebSocket();
                socket.ConnectAsync(uri, CancellationToken.None).Wait();
                return socket;
            }
            catch
            {
                Console.Error.WriteLine($"Failed connecting to '{uri}', trying again in {timeout} seconds");
                Thread.Sleep(TimeSpan.FromSeconds(timeout));

                timeout += step;
                if (timeout > maxTimeout) timeout = maxTimeout;
            }
    }

    private static string ReadUsername()
    {
        while (true)
        {
            Console.Write("Please enter your username: ");
            var username = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(username) && username.Length is >= 1 and <= 64)
                return username;

            Console.WriteLine("Sorry, something went wrong!");
        }
    }

    private static string ReadPassword()
    {
        while (true)
        {
            Console.Write("Please enter your password: ");
            var password = ReadPasswordString();
            if (password.Length is >= 1 and <= 24) return password;
            Console.WriteLine("Sorry, something went wrong!");
        }
    }

    private static string ReadPasswordString(char mask = '*')
    {
        var pwd = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (pwd.Length <= 0) continue;
                pwd.Length--;
                if (mask != '\0')
                    Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                pwd.Append(key.KeyChar);
                if (mask != '\0') Console.Write(mask);
            }
        }

        Console.WriteLine();
        return pwd.ToString();
    }

    public static string DefaultDataPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            var path = home + "/.box";
            Directory.CreateDirectory(path);
            return path;
        }
        throw new PlatformNotSupportedException("Feature is only available on Linux and FreeBSD.");
    }

    public static string DefaultResourcesPath()
    {
        Directory.CreateDirectory(DefaultDataPath() + "/res");
        return DefaultDataPath() + "/res";
    }
}

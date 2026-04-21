using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Crayon.Box.Messages;
using Isopoh.Cryptography.Argon2;
using MySql.Data.MySqlClient;

namespace Crayon.Box;

public class Server
{
    public static readonly Dictionary<string, ClientHandle> Providers = new();
    public static Server? Static = null;

    private readonly WebApplication _app;

    public MySqlConnection MySqlConnection { get; set; }

    public Server(WebApplication app, MySqlConnection sqlConnection)
    {
        Static = this;

        _app = app;
        MySqlConnection = sqlConnection;
        if (app.Environment.IsDevelopment()) app.MapOpenApi();


        // Maps a user's request to a station
        // app.MapGet("/@{id}/{*rest}",
        //     (string id, string rest) => $"Attempting to get '/{rest}' from @{id}" +
        //                                 (Stations.ContainsKey(id) ? 
        //                                     "Miraculously, the station is connected!" : 
        //                                     "Tragically, it was not around"));

        // Begins a client provider connection
        app.Map("/client-clock-in", ProviderClockIn);

        // Gets a WASM turubai page from a provider
        app.MapGet("/@{username}/theme/binaries.wasm", ClientGetWasmPage);
        app.MapGet("/@{username}/resource/{name}", ClientGetResource);

        app.MapPost("/api/user/register-account", UserRegisterAccount);
        app.MapPost("/api/user/login-session", UserLoginSession);
        app.MapPost("/api/user/logout", ConsumerLogout);
        app.MapGet("/api/user/display-name", ProviderGetDisplayName);


        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseWebSockets();
        // app.UseAuthentication();

        app.MapControllers();
        app.MapRazorPages();
        app.MapBlazorHub();

        // Create our databases
        var sqlRequest = """
                            CREATE DATABASE IF NOT EXISTS crayon_box;
                            USE crayon_box;
                            CREATE TABLE IF NOT EXISTS user_table (
                                user_id int PRIMARY KEY AUTO_INCREMENT,
                                username VARCHAR(32) NOT NULL UNIQUE,
                                email VARCHAR(255) NOT NULL UNIQUE,
                                password_hash VARCHAR(255) NOT NULL
                            );

                            CREATE TABLE IF NOT EXISTS session_table (
                                session_id BINARY(16) NOT NULL PRIMARY KEY,
                                user_id INT NOT NULL
                            );
                            """;
        var command = new MySqlCommand(sqlRequest, MySqlConnection);
        command.ExecuteNonQuery();
    }

    public void Run()
    {
        _app.Run();
    }

    private async Task ProviderClockIn(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) return;

        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        // Send the Server's signature
        var utf8Signature = Config.StringEncoding.GetBytes(Config.ServerSignature);
        var segment = new ArraySegment<byte>(utf8Signature);
        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true,
            CancellationToken.None);

        // Send over the Server's info
        var serverVersion = Assembly.GetEntryAssembly()!.GetName().Version;
        var serverInfo = new ServerInformation(serverVersion!.ToString(), Config.Organization);
        var serverInfoEncoded = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(serverInfo));
        var infoMessage = new Message(MessageType.ServerInfo, serverInfoEncoded);

        await infoMessage.SendAsync(webSocket);

        // Get the client signature
        var csBuffer = new byte[Config.ClientSignature.Length];
        await webSocket.ReceiveAsync(csBuffer.AsMemory(), CancellationToken.None);
        var clientSignature = Config.StringEncoding.GetString(csBuffer);

        // Receive the client's info
        var ciMessage = (Message)(await Message.ReceiveAsync(webSocket))!;
        var ciBody = Encoding.UTF8.GetString(ciMessage.RawData.ToArray());
        var ci = JsonSerializer.Deserialize<ClientInformation>(ciBody);
        var cv = ci.ClientVersionParsed;
        Console.WriteLine($"Client Version: {cv}");
        Console.WriteLine($"Client Message: {ciMessage}");

        // Simple Validation
        Console.WriteLine("Signature = " + clientSignature);
        Console.WriteLine("Version = " + cv);
        if (clientSignature != Config.ClientSignature)
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.InvalidPayloadData,
                "Bad signature.",
                CancellationToken.None
            );
            return;
        }

        if (!IsAcceptableClient(cv))
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.EndpointUnavailable,
                "Version not supported.",
                CancellationToken.None
            );
            return;
        }

        // Create a station object & add it to the dictionary
        var station = new ClientHandle(webSocket);
        string? username = null;

        // Poll the station's messages
        while (webSocket.State == WebSocketState.Open)
            // Try to receive data from the websocket
            try
            {
                var message = await Message.ReceiveAsync(webSocket!);
                if (message == null)
                    continue;

                switch (message.Header.Type)
                {
                    case MessageType.PrivilegeElevation:
                        {
                            var authString = Encoding.UTF8.GetString(message.RawData.ToArray());
                            var authorization = JsonSerializer.Deserialize<Authorization>(authString);
                            var sessionId = authorization.SessionID;

                            // Check if the session ID is in the session's table
                            var sql0 = """
                            SELECT u.username FROM session_table s
                            JOIN user_table u ON s.user_id = u.user_id
                            WHERE s.session_id = UUID_TO_BIN(@SessionID);
                            """;
                            var command0 = new MySqlCommand(sql0, MySqlConnection);
                            command0.Parameters.AddWithValue("@SessionID", sessionId);
                            await using (var r = await command0.ExecuteReaderAsync())
                            {
                                Message reply;
                                if (r.Read())
                                {
                                    username = r.GetString(0);
                                    Providers.Add(username, station);
                                    reply = new Message(message, MessageType.AuthSuccessful, []);
                                }
                                else
                                {
                                    reply = new Message(message, MessageType.AuthFailed, []);
                                }
                                await reply.SendAsync(webSocket);
                            }
                            break;
                        }




                    case MessageType.ResourceResponse:
                    case MessageType.WasmPageResponse:
                    case MessageType.DisplayNameResponse:
                        station.HandleMessage(message);
                        break;

                    case MessageType.CloseRequest:
                        if (username != null)
                        {
                            Providers.Remove(username);
                        }
                        await Message.SendCloseOutputAsync(webSocket, WebSocketCloseStatus.Empty, string.Empty);
                        break;

                    // case MessageType.CloseAccepted:
                    //     if (username != null)
                    //         Providers.Remove(username);
                    //     break;


                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (WebSocketException e)
            {
                var ipAddress = context.Connection.RemoteIpAddress;
                if (ipAddress != null)
                    Console.WriteLine(
                        $"An error occured attempting to receive a message from '{ipAddress.ToString()}': {e}");

                if (username != null)
                    Providers.Remove(username);
                break;
            }
    }

    private async Task UserLoginSession(HttpContext context)
    {
        try
        {
            string username = context.Request.Form["username"]!;
            string password = context.Request.Form["password"]!;

            // Fetch stored hash by username, then verify with Argon2
            var sql0 = """
                        SELECT user_id, password_hash FROM user_table WHERE username=@Username;
                        """;

            var command0 = new MySqlCommand(sql0, MySqlConnection);
            command0.Parameters.AddWithValue("@Username", username);

            long userId;
            await using (var r = await command0.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync())
                    throw new BoxException(404, "Username or Password is incorrect.");
                userId = r.GetInt64(0);
                var storedHash = r.GetString(1);
                if (!Argon2.Verify(storedHash, password))
                    throw new BoxException(404, "Username or Password is incorrect.");
            }

            var sessionId = await CreateConsumerSession(userId);
            context.Response.Cookies.Append("session_id", sessionId.ToString());
            await context.Response.WriteAsync(JsonSerializer.Serialize(new LoginResponse(sessionId)));
        }
        catch (BoxException err)
        {
            context.Response.StatusCode = err.Code;
            var json = JsonSerializer.Serialize(new { error = err.Details });
            await context.Response.WriteAsync(json);
        }
    }

    private async Task UserRegisterAccount(HttpContext context)
    {
        string username = context.Request.Form["username"]!;
        string email = context.Request.Form["email"]!;
        string password = context.Request.Form["password"]!;

        var passwordHash = Argon2.Hash(password)!;

        try
        {

            try
            {
                var sql0 = """
                        INSERT INTO user_table (username, email, password_hash)
                        VALUES (@Username, @Email, @PasswordHash);
                        """;
                MySqlCommand command0 = new MySqlCommand(sql0, MySqlConnection);
                command0.Parameters.AddWithValue("@Username", username);
                command0.Parameters.AddWithValue("@Email", email);
                command0.Parameters.AddWithValue("@PasswordHash", passwordHash);
                await command0.ExecuteNonQueryAsync();
            }
            catch (MySqlException e)
            {
                context.Response.StatusCode = 500;
                string message;
                switch (e.Number)
                {
                    case 1062:
                        message = "Username or Email is already taken.";
                        break;

                    default:
                        message = "A miscellaneous database error has occurred.";
                        break;

                }

                throw new BoxException(500, message);
            }

            {
                // Get the user ID.
                var sql1 = """
                SELECT user_id FROM user_table WHERE username = @Username;
                """;
                MySqlCommand command1 = new MySqlCommand(sql1, MySqlConnection);
                command1.Parameters.AddWithValue("@Username", username);

                long userId;
                await using (var r = await command1.ExecuteReaderAsync())
                {
                    if (!await r.ReadAsync())
                        throw new BoxException(500, "User's ID is lost.");
                    userId = r.GetInt64(0);

                } // reader is closed here

                // Now safe to open another command
                var sessionId = await CreateConsumerSession(userId);
                context.Response.Cookies.Append("session_id", sessionId.ToString());
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    session_id = sessionId
                }));
            }
        }
        catch (BoxException err)
        {
            context.Response.StatusCode = err.Code;
            var json = JsonSerializer.Serialize(new { error = err.Details });

            await context.Response.WriteAsync(json);
        }
    }

    private async Task ConsumerLogout(HttpContext context)
    {
        if (context.Request.Cookies["session_id"] is string sessionId)
        {
            var sql = """
                        DELETE FROM session_table WHERE session_id = UUID_TO_BIN(@SessionID);
                        """;
            var command = new MySqlCommand(sql, MySqlConnection);
            command.Parameters.AddWithValue("@SessionID", sessionId);
            await command.ExecuteNonQueryAsync();
            context.Response.Cookies.Delete("session_id");
        }
        context.Response.Redirect("/");
    }

    private async Task<Guid> CreateConsumerSession(long userId)
    {
        var id = Guid.NewGuid();
        var text = """ 
                    INSERT INTO session_table (session_id, user_id)
                    VALUES (UUID_TO_BIN(@Id), @UserID);
                    """;
        var command = new MySqlCommand(text, MySqlConnection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@UserID", userId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<string> GetUsernameAsync(long userId)
    {
        try
        {
            var text = """
                       SELECT username FROM user_table WHERE user_id = @UserID;
                       """;
            var command = new MySqlCommand(text, MySqlConnection);
            command.Parameters.AddWithValue("@UserID", userId);
            await using (var r = await command.ExecuteReaderAsync())
            {
                r.Read();
                return r.GetString(0);
            }
        }
        catch (MySqlException)
        {
            return "";
        }
    }

    public long UserExists(string username)
    {
        var sql = """
                    SELECT user_id FROM user_table WHERE username = @Username; 
                    """;
        var command = new MySqlCommand(sql, MySqlConnection);
        command.Parameters.AddWithValue("@Username", username);
        try
        {
            using var r = command.ExecuteReader();
            r.Read();
            return r.GetInt64(0);
        }
        catch (MySqlException)
        {
            return 0;
        }
    }


    private async Task ProviderGetDisplayName(HttpContext context)
    {
        var username = context.Request.Query["username"].ToString()[1..];

        if (string.IsNullOrEmpty(username) || !Providers.TryGetValue(username, out var client))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var message = new Message(MessageType.GetDisplayName, []);
        var returnMessage = await client.SendMessage(message);
        var displayName = Encoding.UTF8.GetString(returnMessage.RawData.ToArray());

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)returnMessage.Header.StatusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { displayName }));
    }

    private async Task ClientGetWasmPage(HttpContext context)
    {
        var username = context.Request.RouteValues["username"] as string;

        if (username == null || !Providers.TryGetValue(username, out var client))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var message = new Message(MessageType.GetWasmPage, []);
        var returnMessage = await client.SendMessage(message);
        var wasmBytes = returnMessage.RawData.ToArray();

        context.Response.ContentType = "application/wasm";
        context.Response.StatusCode = (int)returnMessage.Header.StatusCode;

        await context.Response.Body.WriteAsync(wasmBytes);
    }

    private async Task ClientGetResource(HttpContext context)
    {
        var username = context.Request.RouteValues["username"] as string;
        var name = context.Request.RouteValues["name"] as string;

        if (username == null || name == null || !Providers.TryGetValue(username, out var client))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var reqData = new RequestResourceInformation { ResourceName = name };
        var reqJson = JsonSerializer.Serialize(reqData);
        var reqBytes = Encoding.UTF8.GetBytes(reqJson);

        var message = new Message(MessageType.GetResource, reqBytes);
        var returnMessage = await client.SendMessage(message);
        var returnBytes = returnMessage.RawData.ToArray();

        // context.Response.ContentType = "application/wasm";
        context.Response.StatusCode = (int)returnMessage.Header.StatusCode;
        await context.Response.Body.WriteAsync(returnBytes);
    }
    private static bool IsAcceptableClient(Version version)
    {
        if (version.Major >= 25) return true;

        return false;
    }
}
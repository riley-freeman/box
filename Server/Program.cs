using System.Diagnostics;
using MySql.Data.MySqlClient;

(string address, string user, string password) GetSqlServerLoginDetails()
{
    // Connect to our database
    string address = "127.0.0.1";
    if (Environment.GetEnvironmentVariable("DATABASE_ADDRESS") != null)
        address = Environment.GetEnvironmentVariable("DATABASE_ADDRESS")!;

    string user = "root";
    if (Environment.GetEnvironmentVariable("DATABASE_USER") != null)
        user = Environment.GetEnvironmentVariable("DATABASE_USER")!;

    string password = "12345";
    if (Environment.GetEnvironmentVariable("DATABASE_PASSWORD") != null)
        password = Environment.GetEnvironmentVariable("DATABASE_PASSWORD")!;

    return (address, user, password);
}


var (databaseAddress, databaseUser, databasePassword) = GetSqlServerLoginDetails();
var connectionString = $"server={databaseAddress};uid={databaseUser};pwd={databasePassword}";

var mysqlConnectionStringBuilder = new MySqlConnectionStringBuilder(connectionString)
{
    Server = databaseAddress,
    UserID = databaseUser,
    Password = databasePassword
};
connectionString = mysqlConnectionStringBuilder.ConnectionString;

// Try to connect to our SQL server
MySqlConnection sqlConn = new MySqlConnection();
try
{
    sqlConn.ConnectionString = connectionString;
    sqlConn.Open(); // This might throw MySqlException.
}
catch (MySqlException ex)
{
    switch (ex.Number)
    {
        case 0:
            Console.Error.WriteLine("Cannot connect to server.  Contact administrator");
            Environment.Exit(1);
            break;
        case 1045:
            Console.Error.WriteLine("Invalid username/password, please try again");
            Environment.Exit(1);
            break;
    }
    throw;
}


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();
var tunnel = new Crayon.Box.Server(app, sqlConn);
tunnel.Run();

using System.CommandLine;
using Crayon.Box;


var client = new ClientApp();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    client.Dispose();
    Environment.Exit(1);
};

var rootCommand = new RootCommand("Manages resources and delivers to the Crayon Box!");

var importCommand = new Command("import", "Imports a new resource file.");
var importPathArg = new Argument<string>("path");
var importNickname = new Option<string>(name: "--name", ["-n"]);
importCommand.Add(importPathArg);
importCommand.Add(importNickname);
importCommand.SetAction(async (parseResult) =>
{
    string path = parseResult.GetValue(importPathArg)!;
    string? nickname = parseResult.GetValue(importNickname);
    await client.ImportFile(path, nickname);
});

var removeCommand = new Command("remove", "Deletes a resource file.");
var removeName = new Argument<string>("cached resource name");
removeCommand.Add(removeName);
removeCommand.SetAction(async (parseResult) =>
{
    string name = parseResult.GetValue(removeName)!;
    await client.RemoveFile(name);
});


var setNameCommand = new Command("set-display-name", "Changes the local display name.");
var setNameArg = new Argument<string>("display name");
setNameCommand.Add(setNameArg);
setNameCommand.SetAction(async (parseResult) =>
{
    string name = parseResult.GetValue(setNameArg)!;
    await client.SetDisplayName(name);
});

rootCommand.Add(importCommand);
rootCommand.Add(removeCommand);
rootCommand.Add(setNameCommand);
rootCommand.SetAction((ParseResult parseResult) =>
{
    var uri = new Uri($"wss://{Config.ServerAddress}:{Config.ServerPort}/client-clock-in");
    client.Serve(uri, null, null);
});

await rootCommand.Parse(args).InvokeAsync();

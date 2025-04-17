using Archipelago.Core;
using System;
using DSAP.Core;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

Console.WriteLine(args.Length);

if (args.Length != 2 && args.Length != 3)
{
    string programName = System.AppDomain.CurrentDomain.FriendlyName;
    Console.WriteLine($"Usage: {programName} <ap_server> <ap_user> [ap_password]");
    return -1;
}

string apServer = args[0];
string apUser = args[1];
string apPassword = args.Length == 3 ? args[2] : "";

var darkSoulsGameConn = new DarkSoulsGameConnection();
var connected = darkSoulsGameConn.Connect();

if (!connected)
{
    Log.Logger.Error("Dark Souls not running, open Dark Souls before connecting!");
    return -2;
}

// Need to do this before checking if we're online to get ArchipelagoClient to set the process ID in helpers
var apClient = new ArchipelagoClient(darkSoulsGameConn);
var darkSoulsClient = new DarkSoulsClient(apClient);

if (Helpers.GetIsPlayerOnline())
{
    Log.Logger.Warning("YOU ARE PLAYING ONLINE. THIS APPLICATION WILL NOT PROCEED.");
    return -3;
}

darkSoulsClient.Connect(apServer, apUser, apPassword);

bool keepRunning = true;

Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    keepRunning = false;
};

while (keepRunning) { }

return 0;
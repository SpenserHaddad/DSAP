using Archipelago.Core.Util;
using Archipelago.Core;
using Serilog;

namespace DSAP.Core
{
    public class DarkSoulsGameConnection : IGameClient
    {
        public bool IsConnected { get; set; }
        public int ProcId { get; set; }
        public string ProcessName { get; set; }
        public DarkSoulsGameConnection()
        {
            ProcessName = "DarkSoulsRemastered";
            ProcId = Memory.GetProcIdFromExe(ProcessName);
        }
        public bool Connect()
        {
            Log.Information($"Connecting to {ProcessName}");
            if (ProcId == 0)
            {
                Log.Error($"{ProcessName} not found.");
                return false;
            }
            IsConnected = true;
            return true;
        }

    }
}

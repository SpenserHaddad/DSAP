using Archipelago.Core.Util;
using Newtonsoft.Json;


namespace DSAP.Core.Models
{
    public class BonfireFlag : EventFlag
    {
        [JsonConverter(typeof(HexToUIntConverter))]
        public uint Offset { get; set; }
        public int AddressBit { get;set; }
        public new ulong Flag 
        {
            get 
            {
                return Helpers.OffsetPointer(Helpers.GetEventFlagsOffset(), (int)Offset); 
            }
        }
    }
}

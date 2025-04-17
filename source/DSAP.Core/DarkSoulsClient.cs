using Archipelago.Core;
using Archipelago.Core.Util;
using Archipelago.Core.Models;
using Archipelago.Core.Traps;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using DSAP.Core.Models;
using Newtonsoft.Json;
using Serilog;
using static DSAP.Core.Enums;


namespace DSAP.Core
{
    public class DarkSoulsClient
    {
        private DeathLinkService? _deathlinkService;
        public ArchipelagoClient Client { get; set; }
        public List<DarkSoulsItem> AllItems { get; set; }
        private readonly object _lockObject = new object();
        private bool IsHandlingDeathlink = false;

        private List<Task>? _monitorTasks;
        private CancellationTokenSource? _cancelMonitorTasksTokenSource;

        public DarkSoulsClient(ArchipelagoClient client)
        {
            AllItems = Helpers.GetAllItems();

            Client = client;
            Client.Connected += OnConnected;
            Client.Disconnected += OnDisconnected;
            Client.ItemReceived += OnItemReceived;
            Client.MessageReceived += OnMessageReceived;

            var isOnline = Helpers.GetIsPlayerOnline();
            if (isOnline)
            {
                Log.Logger.Warning("YOU ARE PLAYING ONLINE. THIS APPLICATION WILL NOT PROCEED.");
                return;
            }
        }

        public async void Connect(string host, string slot, string password = "")
        {
            await Client.Connect(host, "Dark Souls Remastered");
            await Client.Login(slot, password);

            var bossLocations = Helpers.GetBossFlagLocations();
            var itemLocations = Helpers.GetItemLotLocations();
            var bonfireLocations = Helpers.GetBonfireFlagLocations();
            var doorLocations = Helpers.GetDoorFlagLocations();
            var fogWallLocations = Helpers.GetFogWallFlagLocations();
            var miscLocations = Helpers.GetMiscFlagLocations();

            var goalLocation = bossLocations.First(x => x.Name.Contains("Lord of Cinder"));

            _cancelMonitorTasksTokenSource = new CancellationTokenSource();
            _monitorTasks = [
                Task.Factory.StartNew(() => Memory.MonitorAddressBitForAction(goalLocation.Address, goalLocation.AddressBit, () => Client.SendGoalCompletion())),
                Task.Factory.StartNew((foo) => Client.MonitorLocations(bossLocations, _cancelMonitorTasksTokenSource.Token), TaskCreationOptions.LongRunning, _cancelMonitorTasksTokenSource.Token),
                Task.Factory.StartNew((foo) => Client.MonitorLocations(itemLocations, _cancelMonitorTasksTokenSource.Token),TaskCreationOptions.LongRunning, _cancelMonitorTasksTokenSource.Token),
                Task.Factory.StartNew((foo) => Client.MonitorLocations(bonfireLocations, _cancelMonitorTasksTokenSource.Token),TaskCreationOptions.LongRunning, _cancelMonitorTasksTokenSource.Token),
                Task.Factory.StartNew((foo) => Client.MonitorLocations(doorLocations, _cancelMonitorTasksTokenSource.Token),TaskCreationOptions.LongRunning, _cancelMonitorTasksTokenSource.Token),
                Task.Factory.StartNew((foo) => Client.MonitorLocations(fogWallLocations, _cancelMonitorTasksTokenSource.Token),TaskCreationOptions.LongRunning, _cancelMonitorTasksTokenSource.Token),
                Task.Factory.StartNew((foo) => Client.MonitorLocations(miscLocations, _cancelMonitorTasksTokenSource.Token),TaskCreationOptions.LongRunning, _cancelMonitorTasksTokenSource.Token),
            ];

            if ((bool)Client.Options.GetValueOrDefault("enable_deathlink", false))
            {
                _deathlinkService = Client.EnableDeathLink();
                _deathlinkService.OnDeathLinkReceived += OnDeathLinkReceived;
                _monitorTasks.Add(Task.Factory.StartNew((f) =>
                {
                    Memory.MonitorAddressForAction<int>(
                    Helpers.GetPlayerHPAddress(),
                    () => SendDeathlink(_deathlinkService),
                    (health) => Helpers.GetPlayerHP() <= 0);
                },
                    TaskCreationOptions.LongRunning,_cancelMonitorTasksTokenSource.Token
                ));
            }

            RemoveItems();
        }

        public void AddItem(int category, int id, int quantity)
        {
            var command = Helpers.GetItemCommand();
            //Set item category
            Array.Copy(BitConverter.GetBytes(category), 0, command, 0x1, 4);
            //Set item quantity
            Array.Copy(BitConverter.GetBytes(quantity), 0, command, 0x7, 4);
            //set item id
            Array.Copy(BitConverter.GetBytes(id), 0, command, 0xD, 4);

            var result = Memory.ExecuteCommand(command);
        }
        public void AddItemWithMessage(int category, int id, int quantity)
        {
            var command = Helpers.GetItemWithMessage();

            // Set item category (at offset 0x3F)
            Array.Copy(BitConverter.GetBytes(category), 0, command, 0x3F, 4);

            // Set item quantity (at offset 0x43)
            Array.Copy(BitConverter.GetBytes(quantity), 0, command, 0x43, 4);

            // Set item id (at offset 0x47)
            Array.Copy(BitConverter.GetBytes(id), 0, command, 0x47, 4);

            var result = Memory.ExecuteCommand(command);
        }
        public bool IsValidPointer(ulong address)
        {
            try
            {
                Memory.ReadByte(address);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public async Task MonitorLocations(List<Location> locations)
        {
            var locationBatches = locations
                .Select((location, index) => new { Location = location, Index = index })
                .GroupBy(x => x.Index / 25)
                .Select(g => g.Select(x => x.Location).ToList())
                .ToList();
            var tasks = locationBatches.Select(x => MonitorBatch(x));
            await Task.WhenAll(tasks);

        }
        private async Task MonitorBatch(List<Location> batch)
        {
            List<Location> completed = new List<Location>();

            while (!batch.All(x => completed.Any(y => y.Id == x.Id)))
            {
                foreach (var location in batch)
                {
                    var isCompleted = global::Archipelago.Core.Util.Helpers.CheckLocation(location);
                    if (isCompleted)
                    {
                        completed.Add(location);
                        //  Log.Logger.Information(JsonConvert.SerializeObject(location));
                    }
                }
                if (completed.Any())
                {
                    foreach (var location in completed)
                    {
                        Client.SendLocation(location);
                        //     Log.Logger.Information($"{location.Name} ({location.Id}) Completed");
                        batch.Remove(location);
                    }
                }
                completed.Clear();
                await Task.Delay(500);
            }
        }

        private void SendDeathlink(DeathLinkService _deathlinkService)
        {
            if (!IsHandlingDeathlink)
            {
                Log.Logger.Information("Sending Deathlink. RIP.");
                _deathlinkService.SendDeathLink(new DeathLink(Client.CurrentSession.Players.ActivePlayer.Name));
            }

            //Restart deathlink when player is alive again
            Memory.MonitorAddressForAction<int>(Helpers.GetPlayerHPAddress(),
                () =>
                {
                    IsHandlingDeathlink = false;
                    Memory.MonitorAddressForAction<int>(Helpers.GetPlayerHPAddress(),
                        () => SendDeathlink(_deathlinkService),
                        (health) => Helpers.GetPlayerHP() <= 0);
                },
                (health) => Helpers.GetPlayerHP() > 0);
        }

        private void _deathlinkService_OnDeathLinkReceived(DeathLink deathLink)
        {
            Log.Logger.Information("Deathlink received. RIP.");
            IsHandlingDeathlink = true;
            Memory.Write(Helpers.GetPlayerHPAddress(), 0);
        }

        private void RemoveItems()
        {
            var lots = Helpers.GetItemLots();
            var lotFlags = Helpers.GetItemLotFlags();

            //Helpers.WriteToFile("itemLots.json", lots);

            var replacementLot = new ItemLot()
            {
                Rarity = 1,
                GetItemFlagId = -1,
                CumulateNumFlagId = -1,
                CumulateNumMax = 0,
                Items = new List<ItemLotItem>()
                {
                    new ItemLotItem
                    {
                        CumulateLotPoint = 0,
                        CumulateReset = false,
                        EnableLuck = false,
                        GetItemFlagId = -1,
                        LotItemBasePoint = 100,
                        LotItemCategory = (int)DSItemCategory.Consumables,
                        LotItemNum = 1,
                        LotItemId = 370
                    }
                }
            };
            foreach (var lotFlag in lotFlags.Where(x => x.IsEnabled))
            {
                _ = Task.Run(() =>
                {
                    Helpers.OverwriteItemLot(lotFlag.Flag, replacementLot);
                });
            }
            Log.Logger.Information("Finished overwriting items");
        }

        private async void RunLagTrap()
        {
            using (var lagTrap = new LagTrap(TimeSpan.FromSeconds(20)))
            {
                lagTrap.Start();
                await lagTrap.WaitForCompletionAsync();
            }
        }

        private void OnConnected(object? sender, EventArgs args)
        {
            Log.Logger.Information("Connected to Archipelago");
            Log.Logger.Information($"Playing {Client.CurrentSession.ConnectionInfo.Game} as {Client.CurrentSession.Players.GetPlayerName(Client.CurrentSession.ConnectionInfo.Slot)}");
        }

        private void OnDisconnected(object? sender, EventArgs args)
        {
            Log.Logger.Information("Disconnected from Archipelago");
            _cancelMonitorTasksTokenSource?.Cancel();
        }

        private void OnDeathLinkReceived(DeathLink deathLink)
        {
            Log.Logger.Information("Deathlink received. RIP.");
            IsHandlingDeathlink = true;
            Memory.Write(Helpers.GetPlayerHPAddress(), 0);
        }

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            Log.Logger.Information(JsonConvert.SerializeObject(e.Message));
        }

        private void OnItemReceived(object? sender, ItemReceivedEventArgs e)
        {
            var itemId = e.Item.Id;
            var itemToReceive = AllItems.FirstOrDefault(x => x.ApId == itemId);
            if (itemToReceive != null)
            {
                Log.Logger.Verbose($"Received {itemToReceive.Name} ({itemToReceive.ApId})");
                if (itemToReceive.ApId == 11120000)
                {
                    RunLagTrap();
                }
                else AddItem((int)itemToReceive.Category, itemToReceive.Id, 1);
            }
            else
            {
                Log.Logger.Information("Couldn't find correct item");
                var filler = AllItems.First(x => x.Id == 380);
                AddItem((int)filler.Category, filler.Id, 1);
            }
        }
    }
}

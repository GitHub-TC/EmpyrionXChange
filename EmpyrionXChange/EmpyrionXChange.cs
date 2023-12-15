using Eleon.Modding;
using System.Collections.Generic;
using System.Linq;
using EmpyrionNetAPIAccess;
using EmpyrionNetAPIDefinitions;
using EmpyrionNetAPITools;
using System.IO;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Concurrent;
using static EmpyrionXChange.EmpyrionXChange;

namespace EmpyrionXChange
{
    public class ItemBox
    {
        public int itemId { get; set; }
        public string shortcut { get; set; }
        public string fullName { get; set; }
        public int itemCount { get; set; }
    }

    public class XChangeConfig
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public LogLevel LogLevel { get; set; } = LogLevel.Message;
        public string CommandPrefix { get; set; } = "/\\";
        public ItemBox[] AllowedItems { get; set; }
    }

    public class EmpyrionXChange : EmpyrionModBase
    {
        public ConfigurationManager<XChangeConfig> Configuration { get; set; }
        public ModGameAPI DediAPI { get; private set; }

        public Dictionary<int, List<ItemStack>> currentXChange = new Dictionary<int, List<ItemStack>>();

        public enum DialogState
        {
            None,
            InputOpen,
            InputResult,
            OutputOpen,
            OutputResult
        }

        public class XChangeDialogState
        {
            public PlayerInfo P { get; set; }
            [JsonConverter(typeof(StringEnumConverter))]
            public DialogState State { get; set; }
            public ItemBox ItemBox { get; internal set; }
            public ChatInfo Info { get; internal set; }
        }

        public ConcurrentDictionary<int, XChangeDialogState> PlayerXChangeDialogState { get; set; } = new ConcurrentDictionary<int, XChangeDialogState>();


        enum ChatType
        {
            Faction = 3,
            Global = 5,
        }

        public EmpyrionXChange()
        {
            EmpyrionConfiguration.ModName = "EmpyrionXChange";
        }

        public override void Initialize(ModGameAPI dediAPI)
        {
            DediAPI = dediAPI;
            LogLevel = LogLevel.Message;

            Log($"**EmpyrionXChange: loaded");

            LoadConfiuration();
            LogLevel = Configuration.Current.LogLevel;
            ChatCommandManager.CommandPrefix = Configuration.Current.CommandPrefix;
            Event_Player_ItemExchange += HandleXChange;

            ChatCommands.Add(new ChatCommand(@"ex",                HandleOpenXChangeCall, "Hilfe und Status"));
            ChatCommands.Add(new ChatCommand(@"ex (?<command>.+)", HandleOpenXChangeCall, "tausche nach {was}"));
        }
        private void LoadConfiuration()
        {
            Configuration = new ConfigurationManager<XChangeConfig>
            {
                ConfigFilename = Path.Combine(EmpyrionConfiguration.SaveGameModPath, @"XChangeItems.json")
            };

            var DemoInit = !File.Exists(Configuration.ConfigFilename);

            Configuration.Load();

            if (DemoInit) DemoInitConfiguration();

            Configuration.Save();
        }

        private void DemoInitConfiguration()
        {
            Configuration.Current = new XChangeConfig() {
                AllowedItems = new[]
                {
                    new ItemBox(){ itemId=4296, shortcut="mag", fullName="Magnesium" ,  itemCount=99900 },
                    new ItemBox(){ itemId=4298, shortcut="cob", fullName="Cobalt" ,     itemCount=99900 },
                    new ItemBox(){ itemId=4299, shortcut="sil", fullName="Silicon" ,    itemCount=99900 },
                    new ItemBox(){ itemId=4300, shortcut="neo", fullName="Neodymium" ,  itemCount=99900 },
                    new ItemBox(){ itemId=4301, shortcut="cop", fullName="Copper" ,     itemCount=99900 },
                    new ItemBox(){ itemId=4302, shortcut="pro", fullName="Promethium" , itemCount=99900 },
                    new ItemBox(){ itemId=4317, shortcut="ere", fullName="Erestrum" ,   itemCount=99900 },
                    new ItemBox(){ itemId=4318, shortcut="zas", fullName="Zascosium" ,  itemCount=99900 },
                }
            };

            Configuration.Save();
        }

        public void MessagePlayer(int id, string message)
        {
            var outMsg = new IdMsgPrio()
            {
                id  = id,
                msg = message
            };
            Request_InGameMessage_SinglePlayer(outMsg);
        }

        public async Task HandleOpenXChangeCall(ChatInfo info, Dictionary<string, string> args)
        {
            Log($"**HandleOpenXChangeCall {info.type}:{info.msg} {args.Aggregate("", (s, i) => s + i.Key + "/" + i.Value + " ")}");

            if (info.type == (byte)ChatType.Faction) return;

            string cmd;
            args.TryGetValue("command", out cmd);

            switch (cmd)
            {
                default:
                    var currentItem = Configuration.Current.AllowedItems.FirstOrDefault(I => string.Compare(I.shortcut, cmd?.Trim(), StringComparison.InvariantCultureIgnoreCase) == 0);

                    if (currentItem == null) await DisplayHelp(info);
                    else
                    {
                        var player = await Request_Player_Info(info.playerId.ToId());
                        await ItemXChange(info, player, currentItem, DialogState.InputOpen);
                    }

                    break;
                case "help": await DisplayHelp(info); break;
            }
        }

        private async Task DisplayHelp(ChatInfo info)
        {
            await DisplayHelp(info.playerId, 
                Configuration.Current.AllowedItems.Aggregate("", 
                    (S, I) => $"{S}\n{(string.IsNullOrEmpty(ChatCommandManager.CommandPrefix) ? '/' : ChatCommandManager.CommandPrefix.FirstOrDefault())}ex {I.shortcut} => tausche in '{I.fullName}' : Bestand: {I.itemCount}"));
        }

        private async Task ItemXChange(ChatInfo info, PlayerInfo player, ItemBox itemBox, DialogState state)
        {
            var exchange = new ItemExchangeInfo()
            {
                buttonText  = "XChange",
                desc        = "Erze in das Tauschfeld legen und tauschen (ESC oder Button), dann die Erze wieder herausnehmen",
                id          = info.playerId,
                items       = (GetBoxContents(player.entityId) ?? new ItemStack[] { }).Concat(new ItemStack[7*7]).Take(7*7).ToArray(),
                title       = $@"{itemBox.fullName} XChange"
            };

            var newState = new XChangeDialogState
            {
                P         = player,
                Info      = info,
                ItemBox   = itemBox,
                State     = state,
            };

            PlayerXChangeDialogState.AddOrUpdate(player.entityId, newState, (i, s) => newState);

            await Request_Player_ItemExchange(Timeouts.Wait10m, exchange);
        }

        void HandleXChange(ItemExchangeInfo B)
        {
            if (!PlayerXChangeDialogState.TryGetValue(B.id, out var state)) return;
            Log($"**HandleOpenXChangeCall:HandleXChange {state.P.playerName}[{B.id}] -> {state.State}");

            if (state.State == DialogState.None) return;
            if (state.State == DialogState.InputOpen)
            {
                state.State = DialogState.InputResult;
                return;
            }
            if (state.State == DialogState.OutputOpen)
            {
                state.State = DialogState.OutputResult;
                return;
            }

            SetBoxContents(state.P.entityId, state.ItemBox, B.items);
            if (state.State == DialogState.InputResult) ItemXChange(state.Info, state.P, state.ItemBox, DialogState.OutputOpen).GetAwaiter().GetResult();
            else if (state.State == DialogState.OutputResult) state.State = DialogState.None;
        }

        public IEnumerable<ItemStack> GetBoxContents(int playerId)
        {
            return currentXChange.FirstOrDefault(X => X.Key == playerId).Value ?? new List<ItemStack>();
        }

        public void SetBoxContents(int playerId, ItemBox itemBox, ItemStack[] contents)
        {
            if (!currentXChange.ContainsKey(playerId)) currentXChange.Add(playerId, new List<ItemStack>());
            var currentBox = currentXChange[playerId] = contents?.ToList() ?? new List<ItemStack>();

            Log($"**HandleOpenXChangeCall:setBoxContents {currentBox.Aggregate("", (s, i) => s + i.id + "/" + i.count)}");
            currentXChange[playerId] = currentBox.Select(I => XChangeItem(I, itemBox)).ToList();
            Configuration.Save();
        }

        private ItemStack XChangeItem(ItemStack aItem, ItemBox itemBox)
        {
            var sourceItem  = Configuration.Current.AllowedItems.FirstOrDefault(I => I.itemId == itemBox.itemId);
            var destItem    = Configuration.Current.AllowedItems.FirstOrDefault(I => I.itemId == aItem.id);

            Log($"**HandleOpenXChangeCall:xChangeItem {sourceItem?.fullName}:{sourceItem?.itemCount} -> {destItem?.fullName}:{destItem?.itemCount}");
            if (sourceItem == null || destItem == null || sourceItem.itemCount < aItem.count || sourceItem.itemId == destItem.itemId) return aItem;

            sourceItem.itemCount    -= aItem.count;
            destItem.itemCount      += aItem.count;

            aItem.id = itemBox.itemId;

            return aItem;
        }

    }
}

using Eleon.Modding;
using System.Collections.Generic;
using System.Linq;
using EmpyrionNetAPIAccess;
using EmpyrionNetAPIDefinitions;
using EmpyrionNetAPITools;
using System.IO;
using System;
using System.Threading.Tasks;

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
        public ItemBox[] AllowedItems { get; set; }
    }

    public class EmpyrionXChange : EmpyrionModBase
    {
        public ConfigurationManager<XChangeConfig> Configuration { get; set; }
        public ModGameAPI DediAPI { get; private set; }

        public Dictionary<int, List<ItemStack>> currentXChange = new Dictionary<int, List<ItemStack>>();

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
            verbose = true;
            LogLevel = LogLevel.Message;

            log($"**VotingRewardMod: loaded");

            LoadConfiuration();

            ChatCommands.Add(new ChatCommand(@"/ex",                HandleOpenXChangeCall, "Hilfe und Status"));
            ChatCommands.Add(new ChatCommand(@"/ex (?<command>.+)", HandleOpenXChangeCall, "tausche nach {was}"));
        }
        private void LoadConfiuration()
        {
            Configuration = new ConfigurationManager<XChangeConfig>() { UseJSON = true };
            Configuration.ConfigFilename = Path.Combine(EmpyrionConfiguration.SaveGameModPath, @"XChangeItems.json");

            var DemoInit = !File.Exists(Configuration.ConfigFilename);

            Configuration.Load();

            if (DemoInit) DemoInitConfiguration();
        }

        private void DemoInitConfiguration()
        {
            Configuration.Current = new XChangeConfig() {
                AllowedItems = new[]
                {
                    new ItemBox(){ itemId=2248, shortcut="mag", fullName="Magnesium" ,  itemCount=99900 },
                    new ItemBox(){ itemId=2250, shortcut="cob", fullName="Cobalt" ,     itemCount=99900 },
                    new ItemBox(){ itemId=2251, shortcut="sil", fullName="Silicon" ,    itemCount=99900 },
                    new ItemBox(){ itemId=2252, shortcut="neo", fullName="Neodymium" ,  itemCount=99900 },
                    new ItemBox(){ itemId=2253, shortcut="cop", fullName="Copper" ,     itemCount=99900 },
                    new ItemBox(){ itemId=2254, shortcut="pro", fullName="Promethium" , itemCount=99900 },
                    new ItemBox(){ itemId=2269, shortcut="ere", fullName="Erestrum" ,   itemCount=99900 },
                    new ItemBox(){ itemId=2270, shortcut="zas", fullName="Zascosium" ,  itemCount=99900 },
                }
            };

            Configuration.Save();
        }

        public void MessagePlayer(int id, string message)
        {
            var outMsg = new IdMsgPrio()
            {
                id = id,
                msg = message
            };
            Request_InGameMessage_SinglePlayer(outMsg);
        }

        public async Task HandleOpenXChangeCall(ChatInfo info, Dictionary<string, string> args)
        {
            log($"**HandleOpenXChangeCall {info.type}:{info.msg} {args.Aggregate("", (s, i) => s + i.Key + "/" + i.Value + " ")}");

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
                        await ItemXChange(info, player, currentItem, true);
                    }

                    break;
                case "help": await DisplayHelp(info); break;
            }
        }

        private async Task DisplayHelp(ChatInfo info)
        {
            await DisplayHelp(info.playerId, 
                Configuration.Current.AllowedItems.Aggregate("", 
                    (S, I) => $"{S}\n/ex {I.shortcut} => tausche in '{I.fullName}' : Bestand: {I.itemCount}"));
        }

        private async Task ItemXChange(ChatInfo info, PlayerInfo player, ItemBox itemBox, bool aChange)
        {
            var exchange = new ItemExchangeInfo()
            {
                buttonText = "XChange",
                desc = "Erze in das Tauschfeld legen und tauschen (ESC oder Button), dann die Erze wieder herausnehmen",
                id = info.playerId,
                items = GetBoxContents(player.entityId).ToArray(),
                title = $@"{itemBox.fullName} XChange"
            };

            var result = await Request_Player_ItemExchange(int.MaxValue, exchange);

            SetBoxContents(player.entityId, itemBox, result.items);
            if (aChange) await ItemXChange(info, player, itemBox, false);
        }

        public IEnumerable<ItemStack> GetBoxContents(int playerId)
        {
            return currentXChange.FirstOrDefault(X => X.Key == playerId).Value ?? new List<ItemStack>();
        }

        public void SetBoxContents(int playerId, ItemBox itemBox, ItemStack[] contents)
        {
            if (!currentXChange.ContainsKey(playerId)) currentXChange.Add(playerId, new List<ItemStack>());
            var currentBox = currentXChange[playerId] = contents?.ToList() ?? new List<ItemStack>();

            log($"**HandleOpenXChangeCall:setBoxContents {currentBox.Aggregate("", (s, i) => s + i.id + "/" + i.count)}");
            currentXChange[playerId] = currentBox.Select(I => XChangeItem(I, itemBox)).ToList();
            Configuration.Save();
        }

        private ItemStack XChangeItem(ItemStack aItem, ItemBox itemBox)
        {
            var sourceItem = Configuration.Current.AllowedItems.FirstOrDefault(I => I.itemId == itemBox.itemId);
            var destItem = Configuration.Current.AllowedItems.FirstOrDefault(I => I.itemId == aItem.id);

            log($"**HandleOpenXChangeCall:xChangeItem {sourceItem?.fullName}:{sourceItem?.itemCount} -> {destItem?.fullName}:{destItem?.itemCount}");
            if (sourceItem == null || destItem == null || sourceItem.itemCount < aItem.count || sourceItem.itemId == destItem.itemId) return aItem;

            sourceItem.itemCount -= aItem.count;
            destItem.itemCount += aItem.count;

            aItem.id = itemBox.itemId;

            return aItem;
        }

    }
}

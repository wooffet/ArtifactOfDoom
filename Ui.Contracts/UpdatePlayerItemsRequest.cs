using System.Collections.Generic;
using UnityEngine.Networking;

namespace ArtifactOfDoom.Ui.Contracts
{
    public class UpdatePlayerItemsRequest : MessageBase
    {
        public IList<string> ItemNames { get; set; }
        public ItemChangeAction ChangeAction { get; set; }

        public UpdatePlayerItemsRequest(ItemChangeAction itemChangeAction)
        {
            ItemNames = new List<string>();
            ChangeAction = itemChangeAction;
        }

        public void AddItem(string name)
        {
            ItemNames.Add(name);
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write((int)ChangeAction);

            if (ItemNames == null || ItemNames.Count == 0)
            {
                writer.Write(0);
                return;
            }

            writer.Write(ItemNames.Count);
            foreach (var item in ItemNames)
            {
                writer.Write(item);
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            ChangeAction = (ItemChangeAction)reader.ReadInt32();

            int count = reader.ReadInt32();
            if (count > 0)
            {
                ItemNames = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    ItemNames.Add(reader.ReadString());
                }
            }
        }
    }
}

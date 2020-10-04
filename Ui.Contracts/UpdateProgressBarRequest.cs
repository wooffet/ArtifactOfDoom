using UnityEngine.Networking;

namespace ArtifactOfDoom.Ui.Contracts
{
    public class UpdateProgressBarRequest : MessageBase
    {
        // TODO: Not quite sure what the name counter represents
        public double EnemiesKilled { get; set; }
        public double TriggerAmount { get; set; }

        public UpdateProgressBarRequest(double enemiesKilled, double triggerAmount)
        {
            EnemiesKilled = enemiesKilled;
            TriggerAmount = triggerAmount;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(EnemiesKilled);
            writer.Write(TriggerAmount);
        }

        public override void Deserialize(NetworkReader reader)
        {
            EnemiesKilled = reader.ReadDouble();
            TriggerAmount = reader.ReadDouble();
        }
    }
}

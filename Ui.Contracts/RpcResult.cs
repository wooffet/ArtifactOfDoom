using ArtifactOfDoom.Artifacts.Contracts;
using UnityEngine.Networking;

namespace ArtifactOfDoom.Ui.Contracts
{
    public class RpcResult : MessageBase
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public LogSeverity Severity { get; set; }

        public RpcResult()
        {
            Success = false;
            Severity = LogSeverity.None;
        }

        public RpcResult(bool success, string message, LogSeverity severity)
        {
            Success = success;
            Message = message;
            Severity = severity;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Success);
            writer.Write(Message);
            writer.Write((int)Severity);
        }

        public override void Deserialize(NetworkReader reader)
        {
            Success = reader.ReadBoolean();
            Message = reader.ReadString();
            Severity = (LogSeverity)reader.ReadInt32();
        }
    }
}

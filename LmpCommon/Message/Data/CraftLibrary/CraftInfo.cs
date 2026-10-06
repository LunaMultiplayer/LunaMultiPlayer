using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Base;
using System;

namespace LmpCommon.Message.Data.CraftLibrary
{
    public class CraftInfo
    {
        public string FolderName;
        public string CraftName;
        public CraftType CraftType;

        public int NumBytes;
        public byte[] Data = new byte[0];
        public string CraftFolder;

        public void Serialize(NetOutgoingMessage lidgrenMsg)
        {
            lidgrenMsg.Write(FolderName);
            lidgrenMsg.Write(CraftName);
            lidgrenMsg.Write((int)CraftType);

            Common.ThreadSafeCompress(this, ref Data, ref NumBytes);

            lidgrenMsg.Write(NumBytes);
            lidgrenMsg.Write(Data, 0, NumBytes);
            lidgrenMsg.Write(CraftFolder ?? string.Empty);
        }

        public void Deserialize(NetIncomingMessage lidgrenMsg)
        {
            FolderName = lidgrenMsg.ReadString();
            CraftName = lidgrenMsg.ReadString();
            CraftType = (CraftType)lidgrenMsg.ReadInt32();

            NumBytes = lidgrenMsg.ReadInt32();

            var availableBytes = (int)((lidgrenMsg.LengthBits - lidgrenMsg.Position) / 8);
            if (NumBytes < 0 || NumBytes > availableBytes)
                throw new FormatException($"Invalid craft payload of {NumBytes} bytes, only {availableBytes} bytes are left in the message");

            if (Data.Length < NumBytes)
                Data = new byte[NumBytes];

            lidgrenMsg.ReadBytes(Data, 0, NumBytes);

            Common.ThreadSafeDecompress(this, ref Data, NumBytes, out NumBytes);
            CraftFolder = lidgrenMsg.Position < lidgrenMsg.LengthBits ? lidgrenMsg.ReadString() : string.Empty;
        }

        public int GetByteCount()
        {
            return FolderName.GetByteCount() + CraftName.GetByteCount() + sizeof(CraftType) + sizeof(int) + sizeof(byte) * NumBytes + CraftFolder.GetByteCount();
        }
    }
}

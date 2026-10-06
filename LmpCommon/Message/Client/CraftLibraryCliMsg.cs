using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Client.Base;
using LmpCommon.Message.Data.CraftLibrary;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Client
{
    public class CraftLibraryCliMsg : CliMsgBase<CraftLibraryBaseMsgData>
    {
        /// <inheritdoc />
        internal CraftLibraryCliMsg() { }

        /// <inheritdoc />
        public override string ClassName { get; } = nameof(CraftLibraryCliMsg);

        /// <inheritdoc />
        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)CraftMessageType.SyncRequest] = typeof(CraftLibrarySyncRequestMsgData),
            [(ushort)CraftMessageType.CraftData] = typeof(CraftLibraryDataMsgData),
            [(ushort)CraftMessageType.CraftDelete] = typeof(CraftLibraryDeleteMsgData)
        };

        public override ClientMessageType MessageType => ClientMessageType.CraftLibrary;
        protected override int DefaultChannel => 9;
        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}

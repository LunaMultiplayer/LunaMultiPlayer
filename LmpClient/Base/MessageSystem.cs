using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Interface;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace LmpClient.Base
{
    public abstract class MessageSystem<T, TS, TH> : System<T>
        where T : System<T>, new()
        where TS : class, IMessageSender, new()
        where TH : class, IMessageHandler, new()
    {
        /// <summary>
        /// You can set this property to false if your MessageHandler.HandleMessage doesn't call anything 
        /// from Unity and you have your collections as concurrent
        /// </summary>
        protected virtual bool ProcessMessagesInUnityThread => true;

        public TS MessageSender { get; } = new TS();
        public TH MessageHandler { get; } = new TH();

        /// <summary>
        /// Tail of the chain handling the messages outside the Unity thread. A reliable ordered channel
        /// arrives in order, and a later message can depend on an earlier one, so they are chained instead
        /// of each running on its own task
        /// </summary>
        private Task _offThreadHandlingTail = Task.CompletedTask;

        private readonly object _offThreadHandlingLock = new object();

        public virtual void EnqueueMessage(IServerMessageBase msg)
        {
            if (ProcessMessagesInUnityThread)
            {
                MessageHandler.IncomingMessages.Enqueue(msg);
                return;
            }

            lock (_offThreadHandlingLock)
            {
                _offThreadHandlingTail = _offThreadHandlingTail.ContinueWith(_ => HandleMessage(msg), TaskScheduler.Default);
            }
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            //Clear the message queue on disabling
            if (ProcessMessagesInUnityThread)
            {
                MessageHandler.IncomingMessages = new ConcurrentQueue<IServerMessageBase>();
                return;
            }

            //Release the chain so a new session does not queue behind the previous one
            lock (_offThreadHandlingLock)
                _offThreadHandlingTail = Task.CompletedTask;
        }

        protected override void OnEnabled()
        {
            base.OnEnabled();

            //During the update we receive and handle all received messages. 
            //We won't process them until the system is turned ENABLED
            if (ProcessMessagesInUnityThread)
                SetupRoutine(new RoutineDefinition(0, RoutineExecution.Update, ReadAndHandleAllReceivedMessages));
        }

        /// <summary>
        /// Reads all the message queue and calls the handling sub-system
        /// </summary>
        private void ReadAndHandleAllReceivedMessages()
        {
            while (MessageHandler.IncomingMessages.TryDequeue(out var msg))
            {
                HandleMessage(msg);
            }
        }

        private void HandleMessage(IServerMessageBase msg)
        {
            try
            {
                MessageHandler.HandleMessage(msg);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"Error handling message type {msg.Data.GetType()}. Details: {e}");
                NetworkConnection.Disconnect($"Error handling message type {msg.Data.GetType()}. Details: {e}");
            }
            finally
            {
                msg.Recycle();
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.IO;
using UnityEditor;

namespace Unity.LiveCapture.Networking
{
    /// <summary>
    /// A message which can be sent and received over the network. Instances are pooled to avoid
    /// allocations, so it is extremely important not to hold onto message references after they
    /// have been disposed.
    /// </summary>
    class Message : IDisposable
    {
        /// <summary>
        /// When the message contains a data stream larger than this value in bytes, the stream will
        /// be disposed when the message is returned to the pool.
        /// </summary>
        private const int LargeMessageSize = 8 * 1024;

        private static readonly ConcurrentBag<Message> Pool = new ConcurrentBag<Message>();
        private static readonly RecyclableMemoryStreamManager Memory = new RecyclableMemoryStreamManager();

        private Guid _remoteId;
        private ChannelType _channelType;
        private MemoryStream _data;
        private bool _disposed;

        /// <summary>
        /// The remote this message is sent to, or where the message was received from.
        /// </summary>
        public Guid RemoteId
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(Message));

                return _remoteId;
            }
        }

        /// <summary>
        /// The networking channel the message is delivered using.
        /// </summary>
        public ChannelType ChannelType
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(Message));

                return _channelType;
            }
        }

        /// <summary>
        /// The contents of the message.
        /// </summary>
        public MemoryStream Data
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(Message));

                return _data;
            }
        }

        Message()
        {
        }

        /// <summary>
        /// Returns the message to the pool. Be careful not to hold onto message references after disposing,
        /// as the message instance is mutable.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _remoteId = default;
            _channelType = default;

            // To minimize reallocating memory we want to avoid disposing recyclable memory stream instances
            // for messages waiting in the pool. However, this is very inefficient when individual messages
            // are large, as lots of memory will be held by unused messages in the pool. A good balance can be
            // reached by only disposing large memory streams, since they should be much less frequent, while
            // small data streams won't use much memory.
            if (_data.Capacity > LargeMessageSize)
            {
                Memory.FreeStream(_data);
                _data = null;
            }
            else
            {
                _data.SetLength(0);
                _data.Seek(0, SeekOrigin.Begin);
            }

            Pool.Add(this);
        }

        /// <summary>
        /// Gets a <see cref="Message"/> instance from the message pool. This is thread safe.
        /// </summary>
        /// <param name="remoteId">The remote to send the message to. Use <see cref="Networking.Remote.All"/> to send this
        /// message to all connected remotes.</param>
        /// <param name="channelType">The networking channel the message is delivered using.</param>
        /// <param name="dataCapacity">The expected size of the message data. If larger than 0, this value is used to
        /// optimize memory allocation for the data stream.</param>
        /// <returns>A message instance from the pool.</returns>
        public static Message Get(Guid remoteId, ChannelType channelType, int dataCapacity = 0)
        {
            return GetInternal(remoteId, channelType, dataCapacity);
        }

        /// <summary>
        /// Gets a <see cref="Message"/> instance from the message pool. This is thread safe.
        /// </summary>
        /// <param name="remoteId">Where to send the message, or where the message was received from. May
        /// be left as null if not yet known.</param>
        /// <param name="channelType">The networking channel the message is delivered using.</param>
        /// <param name="dataCapacity">The expected size of the message data. If larger than 0, this value is used to
        /// optimize memory allocation for the data stream.</param>
        static Message GetInternal(Guid remoteId, ChannelType channelType, int dataCapacity)
        {
            if (!Pool.TryTake(out var message))
                message = new Message();

            message._remoteId = remoteId;
            message._channelType = channelType;
            message._disposed = false;

            if (message._data == null || !(message.Data.CanRead && message.Data.CanWrite))
                message._data = Memory.GetStream(dataCapacity);
            else
                message._data.Capacity = dataCapacity;

            return message;
        }
    }
}

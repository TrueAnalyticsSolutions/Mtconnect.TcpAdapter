﻿using Mtconnect.AdapterSdk;
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mtconnect
{
    public delegate void TcpConnectionConnected(TcpConnection connection);
    public delegate void TcpConnectionDisconnected(TcpConnection connection, Exception ex = null);
    public delegate bool TcpConnectionDataReceived(TcpConnection connection, string message);
    public class TcpConnection : IDisposable
    {
        private bool _disposing { get; set; } = false;
        private bool _disconnecting { get; set; } = false;

        /// <summary>
        /// An event that fires when the underlying client stream is opened and connected.
        /// </summary>
        public event TcpConnectionConnected OnConnected;

        /// <summary>
        /// An event that fires when the underlying client stream is closed and disconnected.
        /// </summary>
        public event TcpConnectionDisconnected OnDisconnected;

        /// <summary>
        /// An event that fires when data is fully parsed from the underlying client stream. Note that a new line is used to determine the end of a full message.
        /// </summary>
        public event TcpConnectionDataReceived OnDataReceived;

        /// <summary>
        /// Maximum amount of binary data to receive at a time.
        /// </summary>
        private const int BUFFER_SIZE = 4096;

        public ASCIIEncoding Encoder { get; set; } = new ASCIIEncoding();

        /// <summary>
        /// Reference to the <see cref="TcpClient"/> address.
        /// </summary>
        public string ClientId { get; private set; }

        /// <summary>
        /// The period of time (in milliseconds) to timeout stream reading.
        /// </summary>
        public int Heartbeat { get; set; }

        /// <summary>
        /// Reference to the connection to the <see cref="TcpClient"/>.
        /// </summary>
        private TcpClient _client { get; set; }
        
        private Task _receiverThread;
        private CancellationTokenSource _receiverSource;
        private CancellationTokenSource _delaySource;
        
        /// <summary>
        /// Reference to the underlying client stream. Note, only available between <see cref="Connect"/> and <see cref="Disconnect"/> calls.
        /// </summary>
        private NetworkStream _stream { get; set; }

        //
        // Summary:
        //     Reference to a logging service.
        public readonly IAdapterLogger _logger;

        public DateTime? LastCommunicated { get; private set; }

        /// <summary>
        /// Constructs a new TCP connection
        /// </summary>
        /// <param name="client"><inheritdoc cref="TcpClient" path="/summary"/></param>
        /// <param name="heartbeat"><inheritdoc cref="Heartbeat" path="/summary"/></param>
        public TcpConnection(TcpClient client, int heartbeat = 1000, IAdapterLogger logger = default)
        {
            _client = client;
            Heartbeat = heartbeat;
            IPEndPoint clientIp = (IPEndPoint)_client.Client.RemoteEndPoint;
            ClientId = $"{clientIp.Address}:{clientIp.Port}";
            _logger = logger;
        }

        /// <summary>
        /// Connects the underlying client stream and begins receiving data.
        /// </summary>
        public void Connect()
        {
            // Disconnect before attempting to connect again to ensure resources are disposed of
            if (_stream != null) Disconnect();

            _stream = _client.GetStream();

            _receiverSource = new CancellationTokenSource();
            _receiverThread = Task.Factory.StartNew(
                receive,
                _receiverSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );

            if (OnConnected != null) OnConnected(this);
        }

        /// <summary>
        /// Disconnects the underlying client stream and disposes of it. Note that this leaves the connection to the TCP client alone.
        /// </summary>
        public void Disconnect(Exception ex = null)
        {
            if (_client == null || _disconnecting)
                return;

            _logger?.LogDebug("Client {clientId} disconnecting; while disposing: {@disposing}", ClientId, _disposing);
            _disconnecting = true;

            try
            {
                _delaySource?.Cancel();
                _delaySource?.Dispose();
                _delaySource = null;

                _receiverSource?.Cancel();
                _receiverSource?.Dispose();
                _receiverSource = null;
            }
            catch (Exception cancellationException)
            {
                _logger?.LogWarning(cancellationException, "Client {clientId} failed to cancel threads during disconnect", ClientId);
            }

            try
            {
                _receiverThread?.Dispose();
                _receiverThread = null;
            }
            catch (Exception receiverException)
            {
                _logger?.LogWarning(receiverException, "Client {clientId} Failed to dispose of receiver thread during disconnect", ClientId);
            }

            try
            {
                _stream?.Close();
                _stream?.Dispose();
                _stream = null;
            }
            catch (Exception streamException)
            {
                _logger?.LogWarning(streamException, "Client {clientId} Failed to dispose of network stream during disconnect", ClientId);
            }

            try
            {
                if (_client != null)
                {
                    _client?.Close();
                }
                _client = null;
            }
            catch (Exception clientException)
            {
                _logger?.LogWarning(clientException, "Client {clientId} failed to dispose of TCP client and Socket during disconnect", ClientId);
            }

            _disconnecting = false;

            if (OnDisconnected != null)
            {
                _logger?.LogDebug("Client {clientId} disconnected", ClientId);
                OnDisconnected(this, ex);
            }
        }

        /// <summary>
        /// Writes a message to the underlying client stream.
        /// </summary>
        /// <param name="message">Message to send.</param>
        public bool Write(string message) => Write(Encoder.GetBytes(message));
        /// <summary>
        /// Writes a binary message to the underlying client stream.
        /// </summary>
        /// <param name="message">Message to send.</param>
        public bool Write(byte[] message)
        {
            try
            {
                _stream?.Write(message, 0, message.Length);
                LastCommunicated = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                Disconnect(ex);
                return false;
            }
        }

        /// <summary>
        /// Flushes the underlying client stream.
        /// </summary>
        public void Flush()
        {
            _stream?.Flush();
        }

        /// <summary>
        /// Continuously reads messages from the underlying client stream.
        /// </summary>
        private async Task receive()
        {
            Exception ex = null;
            bool heartbeatActive = false;

            byte[] message = new byte[BUFFER_SIZE];
            int length = 0;

            ArrayList readList = new ArrayList();
            ArrayList writeList = new ArrayList();
            _logger?.LogDebug("Client: {clientId} is entering while loop (receive method).", this.ClientId);

            var delay = TimeSpan.FromMilliseconds(500);
            var timeout = TimeSpan.FromMilliseconds(Heartbeat * 4);

            while (_client.Connected)
            {
                _delaySource = new CancellationTokenSource(Heartbeat * 2);
                if (_disconnecting || _disposing || _client == null || _stream == null)
                    break;

                // Check the last time communication occurred between the remote connection. If beyond the timeout, then test the connection with an empty message as a "PING".
                if (LastCommunicated == null)
                    LastCommunicated = DateTime.UtcNow;
                if ((DateTime.UtcNow - LastCommunicated) >= timeout)
                {
                    // Try to send a ping
                    if (!Write("\r"))
                    {
                        _logger?.LogDebug("Client {clientId} breaking connection due to timeout", ClientId);
                        ex = new TimeoutException("TcpConnection heartbeat timed out");
                        break;
                    }
                }

                try
                {
                    if (!_stream.DataAvailable)
                    {
                        await Task.Delay(delay, _delaySource.Token);
                        continue;
                    }

                    int bytesRead = 0;

                    readList.Clear();
                    readList.Add(_client.Client);
                    if (Heartbeat > 0 && heartbeatActive)
                        Socket.Select(readList, null, null, (int)(Heartbeat * 2));
                    if (readList.Count == 0 && heartbeatActive)
                    {
                        ex = new TimeoutException("Heartbeat timed out, closing connection");
                        break;
                    }
                    bytesRead = _stream.Read(message, length, BUFFER_SIZE - length);
                    // Added a check to see if bytesRead is 0. This more reliably reflects the state of the connection.
                    // if bytesRead is 0 the client has gracefully disconnected.
                    if (bytesRead == 0)
                    {
                        _logger?.LogDebug("Client {clientId} is exiting while loop. bytesRead was 0.", this.ClientId);
                        break;
                    }
                    else
                    {
                        _logger?.LogDebug("Client {clientId} has a message ({byteSize} bytes)", ClientId, bytesRead);
                        LastCommunicated = DateTime.UtcNow;
                    }

                    // See if we have a line
                    int pos = length;
                    length += bytesRead;
                    int eol = 0;
                    for (int i = pos; i < length; i++)
                    {
                        if (message[i] == '\n')
                        {

                            String line = Encoder.GetString(message, eol, i);

                            if (OnDataReceived != null)
                                heartbeatActive = OnDataReceived(this, line);

                            eol = i + 1;
                        }
                    }

                    // Remove the lines that have been processed.
                    if (eol > 0)
                    {
                        length = length - eol;
                        // Shift the message array to remove the lines.
                        if (length > 0)
                            Array.Copy(message, eol, message, 0, length);
                    }
                }
                catch (Exception dataAvailableException)
                {
                    _logger?.LogError(dataAvailableException, "Client {clientId} failed to check data availability", ClientId);
                    ex = dataAvailableException;
                    break;
                }



            }
            if (!_disconnecting)
            {
                _logger?.LogDebug("Client {clientId} has exited loop and is disconnecting.", this.ClientId);
                Disconnect(ex);
            }
        }

        public void Dispose()
        {
            _disposing = true;
            Disconnect();
            _disposing = false;
        }
    }
}

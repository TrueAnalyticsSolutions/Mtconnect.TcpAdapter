using Microsoft.Extensions.Logging;
using Mtconnect;
using Mtconnect.AdapterSdk;
using System.Net.Sockets;
using System.Text;

namespace Tests
{
    public class ConnectionTests
    {
        [TearDown]
        public void CleanUp() {
            _logger?.Dispose();
        }
        private ILoggerFactory? _logger;
        private TcpAdapterOptions AdapterOptions { get; set; }
        const int TEST_PORT = 7000;
        const string TEST_HOST = "localhost";

        [SetUp]
        public void Setup()
        {
            _logger = LoggerFactory.Create(o => { o.AddConsole(); o.SetMinimumLevel(LogLevel.Debug); });
            AdapterOptions = new TcpAdapterOptions(heartbeat: 2000, port: TEST_PORT);
        }

        [Test]
        public void TcpClientConnect()
        {
            // Create and start TcpAdapter
            using (var adapter = new TcpAdapter(AdapterOptions, default))
            {
                adapter.Start(new TestAdapterSource());

                // Create and connect a new TcpClient to the TcpAdapter
                using (var client = new TcpClient())
                {
                    client.Connect(TEST_HOST, TEST_PORT);
                    Task.Delay(1000).Wait();

                    Assert.Equals(1, adapter.CurrentConnections);

                    // Listen for a little bit
                    NetworkStream stream = client.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                    DateTime startTime = DateTime.Now;
                    TimeSpan duration = TimeSpan.FromMilliseconds((int)(adapter.Heartbeat * 2.5));
                    Console.WriteLine("ENTERING TIME LOOP");
                    while (DateTime.Now - startTime < duration)
                    {
                        if (stream.DataAvailable)
                        {
                            //char[] buffer = new char[1024];
                            //int receivedData = reader.Read(buffer, 0, 1024);
                            string input = reader.ReadLine();
                            Console.WriteLine($"Received: {input}");
                        }
                    }
                    Console.WriteLine($"EXIT TIME LOOP");
                    Assert.Equals(1, adapter.CurrentConnections);
                    Console.WriteLine($"DISCONNECTING");
                    client.Close();
                }

                Assert.Pass();
            }
        }
        
        [Test]
        public void TcpClientDisconnect()
        {
            // Create and start TcpAdapter
            using(var adapter = new TcpAdapter(AdapterOptions, default))
            {
                adapter.Start(new TestAdapterSource());

                // Create and connect a new TcpClient to the TcpAdapter
                using (var client = new TcpClient())
                {
                    client.Connect(TEST_HOST, TEST_PORT);
                    Task.Delay(1000).Wait();

                    Assert.Equals(1, adapter.CurrentConnections);
                    client.Close();
                }

                // Assert that the TcpClient was successfully removed from the TcpAdapter
                Console.Write("WAITING FOR TIMEOUT...");
                Task.Delay((int)((adapter.Heartbeat+100) * 2)).Wait();
                Console.WriteLine("DONE");
                Console.WriteLine("EVALUATING");
                Assert.Equals(0, adapter.CurrentConnections);

                Assert.Pass();
            }
        }

        [Test]
        public void TcpClientTimeoutDisconnect()
        {
            // Create and start TcpAdapter
            using (var adapter = new TcpAdapter(AdapterOptions, default))
            {
                adapter.Start(new TestAdapterSource());

                // Create and connect a new TcpClient to the TcpAdapter
                using (var client = new TcpClient())
                {
                    client.Connect(TEST_HOST, TEST_PORT);
                    Task.Delay(1000).Wait();

                    Assert.Equals(1, adapter.CurrentConnections);


                    Console.WriteLine("CLOSING CLIENT");
                    client.Close();
                }
                Console.WriteLine("DISPOSED");

                Console.WriteLine("LETTING CLIENT TIMEOUT");
                // Assert that the TcpClient was successfully removed from the TcpAdapter
                Task.Delay((int)((adapter.Heartbeat +100) * 2)).Wait();
                Console.WriteLine("FINISHED TIMEOUT");
                Assert.Equals(0, adapter.CurrentConnections);

                Assert.Pass();
            }
        }

        [Test]
        public void MaxConnectionDenial()
        {
            using (var adapter = new TcpAdapter(AdapterOptions, default))
            {
                adapter.Start(new TestAdapterSource());

                List<TcpClient> clients = new List<TcpClient>();
                for (int i = 0; i < adapter.MaxConnections + 1; i++)
                {
                    var client = new TcpClient();
                    client.Connect(TEST_HOST, TEST_PORT);
                    Task.Delay(1000).Wait();
                    if (i == adapter.MaxConnections)
                    {
                        Assert.Equals(clients.Count, adapter.CurrentConnections);
                    } else
                    {
                        clients.Add(client);
                        Assert.Equals(clients.Count, adapter.CurrentConnections);
                    }
                }

                for (int i = clients.Count - 1; i >= 0; i--)
                {
                    clients[i].Close();
                    clients[i].Dispose();
                }
                Task.Delay(1000).Wait();
                Assert.Equals(0, adapter.CurrentConnections);

                Assert.Pass();
            }
        }
    }
    public class TestAdapterSource : IAdapterSource
    {
        public string DeviceUuid => throw new NotImplementedException();

        public string DeviceName => throw new NotImplementedException();

        public string StationId => throw new NotImplementedException();

        public string SerialNumber => throw new NotImplementedException();

        public string Manufacturer => throw new NotImplementedException();

        public event DataReceivedHandler OnDataReceived;
        public event AdapterSourceStartedHandler OnAdapterSourceStarted;
        public event AdapterSourceStoppedHandler OnAdapterSourceStopped;

        public void Start(CancellationToken token = default)
        {
        }

        public void Stop(Exception ex = null)
        {
        }
    }
}
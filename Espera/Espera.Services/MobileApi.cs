﻿using Espera.Core.Analytics;
using Espera.Core.Management;
using Rareform.Validation;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;

namespace Espera.Services
{
    /// <summary>
    /// Provides methods for connecting mobile endpoints with the application.
    /// </summary>
    public class MobileApi : IDisposable, IEnableLogger
    {
        private readonly object clientListGate;
        private readonly ReactiveList<MobileClient> clients;
        private readonly Library library;
        private readonly int port;
        private bool dispose;
        private TcpListener listener;

        public MobileApi(int port, Library library)
        {
            if (port < 49152 || port > 65535)
                Throw.ArgumentOutOfRangeException(() => port);

            if (library == null)
                Throw.ArgumentNullException(() => library);

            this.port = port;
            this.library = library;
            this.clients = new ReactiveList<MobileClient>();
            this.clientListGate = new object();
        }

        public IObservable<int> ConnectedClients
        {
            get { return this.clients.CountChanged; }
        }

        public void Dispose()
        {
            this.Log().Info("Stopping to listen for incoming connections on port {0}", this.port);

            this.dispose = true;
            this.listener.Stop();

            lock (this.clientListGate)
            {
                foreach (MobileClient client in clients)
                {
                    client.Dispose();
                }

                this.clients.Clear();
            }
        }

        public async Task SendBroadcastAsync()
        {
            var client = new UdpClient();

            IPAddress[] addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;

            while (!this.dispose)
            {
                IEnumerable<IPAddress> localSubnets = addresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork);

                // Get all intern networks and fire our discovery message on the last byte up and
                // down This is the only way to ensure that the clients can discover the server reliably
                foreach (IPAddress ipAddress in localSubnets)
                {
                    byte[] address = ipAddress.GetAddressBytes();
                    byte[] message = Encoding.Unicode.GetBytes("espera-server-discovery");

                    // Start one single task here, instead of creating over 200 tasks for sending
                    await Task.Run(() =>
                    {
                        foreach (int i in Enumerable.Range(1, 254).Where(x => x != address[3]).ToList()) // Save to a list before we change the last address byte
                        {
                            address[3] = (byte)i;

                            client.Send(message, message.Length, new IPEndPoint(new IPAddress(address), this.port));
                        }
                    });
                }

                await Task.Delay(1000);
            }
        }

        public void StartClientDiscovery()
        {
            this.listener = new TcpListener(new IPEndPoint(IPAddress.Any, this.port));
            listener.Start();
            this.Log().Info("Starting to listen for incoming connections on port {0}", this.port);

            Observable.Defer(() => this.listener.AcceptTcpClientAsync().ToObservable())
                .Repeat()
                .Where(x => !this.dispose)
                .Subscribe(socket =>
                {
                    this.Log().Info("New client detected");

                    AnalyticsClient.Instance.RecordMobileUsage();

                    var mobileClient = new MobileClient(socket, this.library);

                    mobileClient.Disconnected.FirstAsync()
                        .Subscribe(x =>
                        {
                            mobileClient.Dispose();

                            lock (this.clientListGate)
                            {
                                this.clients.Remove(mobileClient);
                            }
                        });

                    mobileClient.ListenAsync();

                    lock (this.clientListGate)
                    {
                        this.clients.Add(mobileClient);
                    }
                });
        }
    }
}
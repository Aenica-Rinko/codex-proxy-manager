// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CodexProxyManager
{
    internal static class LoopbackProbe
    {
        internal static bool IsListening(int port, int timeout)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var pending = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    using (pending.AsyncWaitHandle)
                    {
                        if (!pending.AsyncWaitHandle.WaitOne(timeout)) return false;
                        client.EndConnect(pending);
                        return true;
                    }
                }
                catch { return false; }
            }
        }
    }

    internal sealed class DashboardSnapshot
    {
        internal bool Reachable, ReadFailed;
        internal TrafficSnapshot Traffic;
        internal ProxyCatalogSnapshot Catalog;
    }

    // Owns only background polling. The caller marshals to UI and checks its session revision.
    internal sealed class DashboardPoller
    {
        private int pending;
        internal bool IsPending { get { return Interlocked.CompareExchange(ref pending, 0, 0) != 0; } }
        internal bool Begin(int port, NodeControlService nodes, bool refreshNodes, Action<DashboardSnapshot> completed)
        {
            if (nodes == null || completed == null) throw new ArgumentNullException();
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var snapshot = new DashboardSnapshot { Reachable = LoopbackProbe.IsListening(port, 150) };
                    if (snapshot.Reachable)
                    {
                        try { snapshot.Traffic = nodes.ReadTraffic(); if (refreshNodes) snapshot.Catalog = nodes.ReadCatalog(); }
                        catch { snapshot.ReadFailed = true; }
                    }
                    completed(snapshot);
                }
                catch { } // A detached/shutting-down consumer must not crash the polling worker.
                finally { Interlocked.Exchange(ref pending, 0); }
            });
            return true;
        }
    }
}

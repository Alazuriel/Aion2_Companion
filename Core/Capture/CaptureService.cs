
using System;
using System.Linq;
using System.Net;
using PacketDotNet;
using SharpPcap;

namespace AION_2_Companion.Core.Capture;

public sealed class CaptureService : IDisposable
{
    private ICaptureDevice? _device;

    public event Action<CapturedPacket>? PacketReceived;

    public static ICaptureDevice[] GetDevices()
        => CaptureDeviceList.Instance.ToArray();

    public void Start(ICaptureDevice device)
    {
        if (_device is not null)
            throw new InvalidOperationException(
                "Capture is already running.");

        _device = device;

        try
        {
            _device.OnPacketArrival += OnPacketArrival;
            _device.Open(DeviceModes.Promiscuous, 1000);
            // Filter in the capture driver, before PacketDotNet and the UI ever see
            // the packet. Capturing every TCP/UDP packet on the machine caused a
            // very high allocation rate and eventually forced Windows to grow the
            // page file on long sessions.
            _device.Filter = "tcp port 13328";
            _device.StartCapture();
        }
        catch
        {
            _device.OnPacketArrival -= OnPacketArrival;

            try { _device.Close(); }
            catch { /* Preserve the original error. */ }

            _device = null;
            throw;
        }
    }



private void OnPacketArrival(
    object sender,
    PacketCapture capture)
{
    try
    {
        var raw = capture.GetPacket();
        var packet = Packet.ParsePacket(
            raw.LinkLayerType,
            raw.Data);

        var ipv4 = packet.Extract<IPv4Packet>();
        var ipv6 = packet.Extract<IPv6Packet>();

        IPAddress? source = ipv4?.SourceAddress
                            ?? ipv6?.SourceAddress;

        IPAddress? destination = ipv4?.DestinationAddress
                                 ?? ipv6?.DestinationAddress;

        if (source is null || destination is null)
            return;

        string protocol;
        int sourcePort;
        int destinationPort;
        byte[] payload;

        byte tcpFlags = 0;
        uint? tcpSequenceNumber = null;
        uint? tcpAcknowledgmentNumber = null;

        var tcp = packet.Extract<TcpPacket>();

        if (tcp is not null)
        {
            protocol = "TCP";
            sourcePort = tcp.SourcePort;
            destinationPort = tcp.DestinationPort;
            payload = tcp.PayloadData ?? Array.Empty<byte>();

            tcpSequenceNumber = tcp.SequenceNumber;
            tcpAcknowledgmentNumber = tcp.AcknowledgmentNumber;

            if (tcp.Finished)       tcpFlags |= 0x01;
            if (tcp.Synchronize)    tcpFlags |= 0x02;
            if (tcp.Reset)          tcpFlags |= 0x04;
            if (tcp.Push)           tcpFlags |= 0x08;
            if (tcp.Acknowledgment) tcpFlags |= 0x10;
            if (tcp.Urgent)         tcpFlags |= 0x20;
        }
        else
        {
            var udp = packet.Extract<UdpPacket>();

            if (udp is null)
                return;

            protocol = "UDP";
            sourcePort = udp.SourcePort;
            destinationPort = udp.DestinationPort;
            payload = udp.PayloadData ?? Array.Empty<byte>();
        }

        if (sender is not ICaptureDevice device)
            return;

        PacketReceived?.Invoke(new CapturedPacket(
            new DateTimeOffset(raw.Timeval.Date),
            device.Name,
            $"{source}:{sourcePort}",
            $"{destination}:{destinationPort}",
            payload,
            protocol,
            tcpSequenceNumber,
            tcpAcknowledgmentNumber,
            tcpFlags));
    }
    catch
    {
        // Egy hibás csomag ne állítsa le a capture-t.
    }
}



    public void Stop()
    {
        var device = _device;
        if (device is null)
            return;

        _device = null;
        device.OnPacketArrival -= OnPacketArrival;

        try
        {
            if (device.Started)
                device.StopCapture();
        }
        finally
        {
            device.Close();
        }
    }

    public void Dispose() => Stop();
}

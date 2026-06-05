using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RemoteManager.Services;

public static class WakeOnLanService
{
    public static async Task WakeUpAsync(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            throw new ArgumentException("MAC address cannot be empty.");

        // Clean up the MAC address
        string cleanMac = Regex.Replace(macAddress, "[-|:]", "");
        if (cleanMac.Length != 12)
            throw new ArgumentException("Invalid MAC address format.");

        byte[] macBytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            macBytes[i] = Convert.ToByte(cleanMac.Substring(i * 2, 2), 16);
        }

        // Magic packet: 6 bytes of 0xFF followed by 16 repetitions of the MAC address
        byte[] magicPacket = new byte[102];
        for (int i = 0; i < 6; i++)
        {
            magicPacket[i] = 0xFF;
        }
        for (int i = 1; i <= 16; i++)
        {
            Array.Copy(macBytes, 0, magicPacket, i * 6, 6);
        }

        // Send WOL packet
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        await client.SendAsync(magicPacket, magicPacket.Length, new IPEndPoint(IPAddress.Broadcast, 9));
    }
}

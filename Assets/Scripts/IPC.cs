using System;
using System.Threading;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Lachee.IO;

public class IpcClient
{
    public Action<IpcPacket> OnReceive;

    private NamedPipeClientStream stream;

    string GetPipeName(int id)
    {
        return $"discord-ipc-{id}";
    }

    public async UniTask Connect(CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                stream = new NamedPipeClientStream(".", GetPipeName(i));
                await UniTask.RunOnThreadPool(() => stream.Connect(), cancellationToken: cancellationToken);
                Debug.Log($"IPC Connected: " + GetPipeName(i));
                BeginReceiving(cancellationToken);
                break;
            }
            catch (Exception e)
            {
                if (i == 9)
                {
                    Debug.LogError("Failed to connect IPC: " + e.Message);
                    return;
                }
                continue;
            }
        }
    }

    void BeginReceiving(CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new InvalidOperationException();
        UniTask.RunOnThreadPool(async () =>
        {
            while (stream.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var buffer = new byte[4096];
                var len = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (len > 0) OnReceive?.Invoke(new IpcPacket(buffer, len));
                await UniTask.Delay(100);
            }
        }, cancellationToken: cancellationToken);
    }

    public async UniTask Send(IpcPacket packet, CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new InvalidOperationException();
        var bytes = packet.ToBytes();
        await UniTask.RunOnThreadPool(() =>
        {
            stream.Write(bytes, 0, bytes.Length);
        }, cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        if (stream != null)
        {
            stream.Disconnect();
            stream.Close();
            stream.Dispose();
        }
    }
}

public class IpcPacket
{
    public enum Opcodes
    {
        HANDSHAKE = 0x0000,
        FRAME = 0x0001,
        CLOSE = 0x0002,
        PING = 0x0003,
        PONG = 0x0004
    }

    public IpcPacket(Opcodes opcode, JObject data)
    {
        this.opcode = opcode;
        this.data = data;
    }
    public IpcPacket(byte[] bytes, int size)
    {
        opcode = (Opcodes)BitConverter.ToUInt32(bytes, 0);
        uint length = BitConverter.ToUInt32(bytes, sizeof(uint));
        if (sizeof(uint) + sizeof(uint) + length > size) throw new Exception();
        string jsonString = Encoding.UTF8.GetString(bytes, sizeof(uint) + sizeof(uint), (int)length);
        data = JObject.Parse(jsonString);
    }
    public byte[] ToBytes()
    {
        byte[] json = Encoding.UTF8.GetBytes(data.ToString(Formatting.None));
        byte[] op = BitConverter.GetBytes((uint)opcode);
        byte[] len = BitConverter.GetBytes(json.Length);

        byte[] buff = new byte[op.Length + len.Length + json.Length];
        op.CopyTo(buff, 0);
        len.CopyTo(buff, op.Length);
        json.CopyTo(buff, op.Length + len.Length);
        return buff;
    }
    public override string ToString()
    {
        return $"[Opcode: {opcode.ToString()}, Data: {data.ToString()}]";
    }

    public Opcodes opcode;
    public JObject data;
}

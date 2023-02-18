using System;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class DiscordRpc : MonoBehaviour
{

    public string clientId;
    public string clientSecret;
    public string[] scopes;

    IpcClient client;
    public bool isConnected { get; private set; } = false;

    async void Start()
    {
        //var cancellationToken = this.GetCancellationTokenOnDestroy();
        client = new IpcClient();

        await client.Connect();
        isConnected = true;
        Debug.Log(await Handshake());
        var authorizeData = await Authorize();
        string code = authorizeData.data["data"]?["code"]?.Value<string>();
        if (code == null) throw new Exception("Failed to get code.");
        Debug.Log(code);
        string accessToken = await FetchAccessToken(code);
        Debug.Log(await Authenticate(accessToken));
        bool? muted = await GetMute();
        await SetMute((!muted) ?? true);
        Debug.Log(await Subscribe("VOICE_SETTINGS_UPDATE"));
        Debug.Log(await client.Receive());
    }

    void OnDestroy()
    {
        if (client != null)
            client.Dispose();
    }

    async UniTask<IpcPacket> SendForRequest(IpcPacket packet)
    {
        await client.Send(packet);
        return await client.Receive();
    }

    public async UniTask<IpcPacket> Handshake()
    {
        JObject handshake = new JObject
        {
            ["v"] = 1,
            ["client_id"] = clientId
        };
        IpcPacket packet = new IpcPacket(IpcPacket.Opcodes.HANDSHAKE, handshake);
        return await SendForRequest(packet);
    }

    public async UniTask<IpcPacket> Authorize()
    {
        JObject content = new JObject
        {
            ["cmd"] = "AUTHORIZE",
            ["args"] = new JObject
            {
                ["scopes"] = new JArray(scopes),
                ["client_id"] = clientId
            },
            ["nonce"] = Nonce()
        };
        return await SendForRequest(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
    }

    public async UniTask<string> FetchAccessToken(string code)
    {
        WWWForm form = new WWWForm();
        form.AddField("client_id", clientId);
        form.AddField("client_secret", clientSecret);
        form.AddField("code", code);
        form.AddField("grant_type", "authorization_code");
        form.AddField("redirect_url", "http://localhost");
        var request = UnityWebRequest.Post("https://discord.com/api/oauth2/token", form);
        await request.SendWebRequest();
        JObject json = JObject.Parse(request.downloadHandler.text);
        if (json["access_token"] == null) throw new Exception();
        return json["access_token"].Value<string>();
    }

    public async UniTask<IpcPacket> Authenticate(string accessToken)
    {
        JObject content = new JObject
        {
            ["cmd"] = "AUTHENTICATE",
            ["args"] = new JObject
            {
                ["access_token"] = accessToken
            },
            ["nonce"] = Nonce()
        };
        return await SendForRequest(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
    }

    public async UniTask<bool?> GetMute()
    {
        JObject content = new JObject
        {
            ["cmd"] = "GET_VOICE_SETTINGS",
            ["args"] = new JObject { },
            ["nonce"] = Nonce()
        };
        var setting = await SendForRequest(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
        return setting.data["data"]?["mute"]?.Value<bool>();
    }

    public async UniTask<IpcPacket> SetMute(bool mute)
    {
        JObject content = new JObject
        {
            ["cmd"] = "SET_VOICE_SETTINGS",
            ["args"] = new JObject
            {
                ["mute"] = mute
            },
            ["nonce"] = Nonce()
        };
        return await SendForRequest(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
    }

    public async UniTask<IpcPacket> Subscribe(string evt)
    {
        JObject content = new JObject
        {
            ["cmd"] = "SUBSCRIBE",
            ["args"] = new JObject { },
            ["evt"] = evt,
            ["nonce"] = Nonce()
        };
        return await SendForRequest(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
    }

    public async UniTask<IpcPacket> Close()
    {
        Debug.Log(new IpcPacket(IpcPacket.Opcodes.CLOSE, new JObject()));
        return await SendForRequest(new IpcPacket(IpcPacket.Opcodes.CLOSE, new JObject()));
    }

    string Nonce()
    {
        return Guid.NewGuid().ToString();
    }
}
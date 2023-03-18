using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using System.Collections;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class DiscordRpc : MonoBehaviour
{
    public string[] scopes;

    [Serializable]
    public class BoolEvent : UnityEvent<bool> { }

    [Space]
    public BoolEvent onMuteChanged;

    IpcClient client = new IpcClient();
    public bool isConnected { get; private set; } = false;

    string clientId { get { return SettingsProvider.settings.clientId; } }
    string clientSecret { get { return SettingsProvider.settings.clientSecret; } }
    string accessToken { get { return SettingsProvider.settings.accessToken; } set { SettingsProvider.settings.accessToken = value; } }

    /// <summary>
    /// Discordから受信したパケットのリスナー
    /// </summary>
    struct Listener
    {
        /// <summary>
        /// 返答を待っているリクエストのnonce、またはSubscribeしたイベントのイベント名、またはOpcode名(FRAME以外の場合)
        /// </summary>
        public string identifier;
        /// <summary>
        /// レスポンス後にこのリスナーを削除するか(イベントの場合はfalse、それ以外の場合はtrueを想定)
        /// </summary>
        public bool once;
        public Action<IpcPacket> callback;
        public Listener(string identifier, bool once, Action<IpcPacket> callback)
        {
            this.identifier = identifier;
            this.once = once;
            this.callback = callback;
        }
    }
    List<Listener> listeners = new List<Listener>();

    async void Start()
    {
        client.OnReceive = OnReceive;

        await client.Connect(this.GetCancellationTokenOnDestroy());
        await Handshake();

        try
        {
            if (accessToken == "") throw new Exception();
            else await Authenticate(accessToken);
        }
        catch
        {
            // 初回起動である等の理由でAccessTokenが存在しないか不正である場合はAuthorizeから行う
            var authorizeData = await Authorize();
            string code = authorizeData.data["data"]?["code"]?.Value<string>();
            if (code == null) throw new Exception("Failed to get code.");
            accessToken = await FetchAccessToken(code);

            await Authenticate(accessToken);
        }

        isConnected = true;

        Debug.Log("Muted: " + await GetMute());
        await Subscribe("VOICE_SETTINGS_UPDATE", OnVoiceSettingsUpdated);
    }

    void OnDisable()
    {
        if (client != null)
            client.Dispose();
    }

    void OnDestroy()
    {
        if (client != null)
            client.Dispose();
    }

    #region イベント処理

    /// <summary>
    /// IpcClientから受信したパケットを処理します。
    /// </summary>
    /// <param name="packet"></param>
    void OnReceive(IpcPacket packet)
    {
        // nonceを取得、なければイベント名を取得
        var identifier = packet.data["nonce"]?.Value<string>();
        if (identifier == null) identifier = packet.data["evt"]?.Value<string>();
        if (identifier == null) throw new Exception("Neither nonce nor evt could be found.");
        listeners.FindAll((l) => l.identifier == identifier).ForEach((l) =>
        {
            l.callback(packet);
            if (l.once) listeners.Remove(l);
        });
    }

    void OnVoiceSettingsUpdated(IpcPacket packet)
    {
        bool? muted = packet.data["data"]?["mute"]?.Value<bool>();
        Debug.Log("Muted: " + muted);
        if (muted != null) onMuteChanged.Invoke((bool)muted);
    }

    /// <summary>
    /// パケットを送信し、それに対する返答を待ちます。
    /// </summary>
    /// <param name="packet"></param>
    /// <returns></returns>
    async UniTask<IpcPacket> SendForResponse(IpcPacket packet) {
        // FRAMEの場合はnonceを、それ以外の場合はOpcodeを識別子とする
        string identifier = packet.opcode == IpcPacket.Opcodes.FRAME ? packet.data["nonce"]?.Value<string>() : packet.opcode.ToString();
        if (identifier == null) throw new Exception("Nonce is not set.");
        await client.Send(packet, this.GetCancellationTokenOnDestroy());

        IpcPacket response = null;
        Action<IpcPacket> listener = (p) => response = p;
        listeners.Add(new Listener(identifier, true, listener));
        await UniTask.WaitUntil(() => response != null, cancellationToken: this.GetCancellationTokenOnDestroy());
        return response;
    }

    #endregion

    #region Discordに対する各種操作

    public async UniTask<IpcPacket> Handshake()
    {
        JObject handshake = new JObject
        {
            ["v"] = 1,
            ["client_id"] = clientId
        };
        IpcPacket packet = new IpcPacket(IpcPacket.Opcodes.HANDSHAKE, handshake);
        await client.Send(packet, this.GetCancellationTokenOnDestroy());

        IpcPacket response = null;
        Action<IpcPacket> listener = (p) => response = p;
        listeners.Add(new Listener("READY", true, listener));
        await UniTask.WaitUntil(() => response != null, cancellationToken: this.GetCancellationTokenOnDestroy());
        return response;
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
        return await SendForResponse(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
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
        IpcPacket response = await SendForResponse(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
        if (response.data["evt"].Value<string>() == "ERROR") throw new Exception(response.data["data"]?["message"]?.Value<string>());
        return response;
    }

    public async UniTask<bool?> GetMute()
    {
        JObject content = new JObject
        {
            ["cmd"] = "GET_VOICE_SETTINGS",
            ["args"] = new JObject { },
            ["nonce"] = Nonce()
        };
        var setting = await SendForResponse(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
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
        return await SendForResponse(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
    }

    public async UniTask<IpcPacket> Subscribe(string evt, Action<IpcPacket> callback)
    {
        JObject content = new JObject
        {
            ["cmd"] = "SUBSCRIBE",
            ["args"] = new JObject { },
            ["evt"] = evt,
            ["nonce"] = Nonce()
        };
        var res = await SendForResponse(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
        listeners.Add(new Listener(evt, false, callback));
        return res;
    }
    public async UniTask<IpcPacket> Unsubscribe(string evt, Action<IpcPacket> callback)
    {
        // TODO: 一つのイベントに複数のリスナーを登録している場合にはUNSUBSCRIBEを送らない
        JObject content = new JObject
        {
            ["cmd"] = "UNSUBSCRIBE",
            ["args"] = new JObject { },
            ["evt"] = evt,
            ["nonce"] = Nonce()
        };
        var res = await SendForResponse(new IpcPacket(IpcPacket.Opcodes.FRAME, content));
        listeners.Remove(new Listener(evt, false, callback));
        return res;
    }

    public async UniTask<IpcPacket> Close()
    {
        Debug.Log(new IpcPacket(IpcPacket.Opcodes.CLOSE, new JObject()));
        return await SendForResponse(new IpcPacket(IpcPacket.Opcodes.CLOSE, new JObject()));
    }

    #endregion

    string Nonce()
    {
        return Guid.NewGuid().ToString();
    }
}
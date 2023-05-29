using UnityEngine;
using Unity.WebRTC;
using UnityWebSocket;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class WebRtcSender : MonoBehaviour
{
    #region Field

    [SerializeField] private float frameRatio = 30f; //画面传输的帧率

    private Camera[] cameras;
    private RTCPeerConnection peerConnection;
    private List<RTCRtpSender> pc1Senders;
    private MediaStream[] videoStreams;
    private bool videoUpdateStarted;
    private WebSocket webSocket;
    protected RTCPeerConnection PeerConnection
    {
        get
        {
            return peerConnection;
        }
        set
        {
            peerConnection = value;
        }
    }

    private RenderTexture tempRenderTexture;
    private RenderTextureFormat renderTextureFormat;
    private TextureFormat textureFormat;
    private List<GraphicsFormat> supportedFormats;

    #endregion

    #region LifeCycle

    private void Awake()
    {
        if (cameras == null || cameras.Length <= 0)
        {
            ResetCamera();
            if (cameras == null)
            {
                return;
            }
        }

        supportedFormats = new();
        foreach (GraphicsFormat formats in Enum.GetValues(typeof(GraphicsFormat)))
        {
            if (SystemInfo.IsFormatSupported(formats, FormatUsage.Render))
            {
                supportedFormats.Add(formats);
            }
        }
        textureFormat = GraphicsFormatUtility.GetTextureFormat(supportedFormats[0]);
        renderTextureFormat = GraphicsFormatUtility.GetRenderTextureFormat(supportedFormats[0]);

        pc1Senders = new List<RTCRtpSender>();
        InitP2P();
    }

    private void Start()
    {
        InitializeWebSocket();
        CaptureStream();
    }

    private void OnDestroy()
    {
        HangUp();
    }

    #endregion

    #region WebRTC

    /// <summary>
    /// 视频流注册刷新
    /// </summary>
    private void CaptureStream()
    {
        MediaStream videoStream = new();
        int depthValue = (int)RenderTextureDepth.Depth24;
        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);

        tempRenderTexture = new RenderTexture(Screen.width, Screen.height, depthValue, format);

        var videoStreamTrack = new VideoStreamTrack(tempRenderTexture, false);
        videoStream.AddTrack(videoStreamTrack);

        foreach (var track in videoStream.GetTracks())
        {
            pc1Senders.Add(PeerConnection.AddTrack(track, videoStream));
        }

        StartCoroutine(GrabScreenTextureAndUpdateStream(tempRenderTexture));

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }

    /// <summary>
    /// 实时截取屏幕画面
    /// </summary>
    /// <param name="renderTexture"></param>
    /// <returns></returns>
    private IEnumerator GrabScreenTextureAndUpdateStream(RenderTexture renderTexture)
    {
        WaitForSecondsRealtime waitForEndOfFrame = new WaitForSecondsRealtime(1f / frameRatio);

        while (Application.isPlaying)
        {
            yield return waitForEndOfFrame;

            int depthValue = (int)RenderTextureDepth.Depth24;
            renderTexture = RenderTexture.GetTemporary(Screen.width, Screen.height, depthValue, renderTextureFormat);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(renderTexture);
            AsyncGPUReadback.Request(renderTexture, 0, textureFormat, OnCompleteReadback);
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    /// <summary>
    /// AsyncGPUReadback回调
    /// </summary>
    /// <param name="request"></param>
    void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }

        if (Application.isPlaying)
        {
            var tex = new Texture2D(Screen.width, Screen.height, textureFormat, false);
            tex.LoadRawTextureData(request.GetData<uint>());
            tex.Apply();
            Graphics.Blit(tex, tempRenderTexture);
            Destroy(tex);
        }
    }

    /// <summary>
    /// 重新启动 ICE
    /// </summary>
    private void RestartIce()
    {
        InitP2P();
        PeerConnection.RestartIce();
    }

    /// <summary>
    /// 处理ICE候选事件
    /// </summary>
    /// <param name="candidate"></param>
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new RTCIceCandidateConverter());
        var content = JsonConvert.SerializeObject(candidate, settings);
        var msg = new Msg(1, content);
        // 用WebSocket发送ICE候选
        webSocket.SendAsync(msg.ToString());
        Debug.Log("OnIceCandidate  " + msg.ToString());
    }

    /// <summary>
    /// ICE连接状态变化处理
    /// </summary>
    /// <param name="state"></param>
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"OnIceConnectionChange: {state}");

        if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
        {
            StartCoroutine(CheckStats());
        }
    }

    /// <summary>
    /// 协商触发事件
    /// </summary>
    private void OnNegotiationNeeded()
    {
        StartCoroutine(PeerNegotiationNeeded());
    }

    /// <summary>
    /// 创建Offer成功
    /// </summary>
    /// <param name="desc"></param>
    /// <returns></returns>
    private IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"Offer from sender \n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        // 设置本地描述
        var op = PeerConnection.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            // 设置本地描述成功
            OnSetLocalSuccess(PeerConnection);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        // 使用WebSocket发送创建Offer成功消息到接收端
        var msg = new Msg(2, desc);
        webSocket.SendAsync(msg.ToString());
    }

    /// <summary>
    /// 设置本地描述成功
    /// </summary>
    /// <param name="pc"></param>
    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetLocalDescription complete");
    }

    /// <summary>
    /// 设置会话描述错误处理
    /// </summary>
    /// <param name="error"></param>
    private void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        HangUp();
    }

    /// <summary>
    /// 设置远程描述成功
    /// </summary>
    /// <param name="pc"></param>
    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetRemoteDescription complete");
    }

    /// <summary>
    /// 创建应答成功处理
    /// </summary>
    /// <param name="desc"></param>
    /// <returns></returns>
    private IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"setRemoteDescription start");

        //设置远程会话描述
        var op2 = PeerConnection.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(PeerConnection);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }
    }

    /// <summary>
    /// 创建会话描述错误处理
    /// </summary>
    /// <param name="error"></param>
    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }

    /// <summary>
    /// 检查状态协程
    /// </summary>
    /// <returns></returns>
    private IEnumerator CheckStats()
    {
        yield return new WaitForSeconds(0.1f);
        if (PeerConnection == null)
            yield break;

        var op = PeerConnection.GetStats();
        yield return op;
        if (op.IsError)
        {
            Debug.LogErrorFormat("RTCPeerConnection.GetStats failed: {0}", op.Error);
            yield break;
        }

        RTCStatsReport report = op.Value;
        RTCIceCandidatePairStats activeCandidatePairStats = null;
        RTCIceCandidateStats remoteCandidateStats = null;

        foreach (var transportStatus in report.Stats.Values.OfType<RTCTransportStats>())
        {
            if (report.Stats.TryGetValue(transportStatus.selectedCandidatePairId, out var tmp))
            {
                activeCandidatePairStats = tmp as RTCIceCandidatePairStats;
            }
        }

        if (activeCandidatePairStats == null || string.IsNullOrEmpty(activeCandidatePairStats.remoteCandidateId))
        {
            yield break;
        }

        foreach (var iceCandidateStatus in report.Stats.Values.OfType<RTCIceCandidateStats>())
        {
            if (iceCandidateStatus.Id == activeCandidatePairStats.remoteCandidateId)
            {
                remoteCandidateStats = iceCandidateStatus;
            }
        }

        if (remoteCandidateStats == null || string.IsNullOrEmpty(remoteCandidateStats.Id))
        {
            yield break;
        }

        Debug.Log($"candidate stats Id:{remoteCandidateStats.Id}, Type:{remoteCandidateStats.candidateType}");
    }

    /// <summary>
    /// 协商协程
    /// </summary>
    /// <returns></returns>
    private IEnumerator PeerNegotiationNeeded()
    {
        // 创建offer
        var op = PeerConnection.CreateOffer();
        yield return op;

        // 如果没有错误
        if (!op.IsError)
        {
            // 如果信令状态不稳定
            if (PeerConnection.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"signaling state is not stable.");
                yield break;
            }

            // 创建Offer成功处理
            yield return StartCoroutine(OnCreateOfferSuccess(op.Desc));
        }
        else
        {
            // 如果发生错误，调用错误处理函数
            OnCreateSessionDescriptionError(op.Error);
        }
    }

    /// <summary>
    /// 移除视频跟踪
    /// </summary>
    private void RemoveTracks()
    {
        if (pc1Senders != null && pc1Senders.Count > 0)
        {
            foreach (var sender in pc1Senders)
            {
                PeerConnection.RemoveTrack(sender);
            }

            pc1Senders.Clear();
        }
    }

    #endregion

    #region WebSocket

    /// <summary>
    /// 初始化WebSocket
    /// </summary>
    private void InitializeWebSocket()
    {
        webSocket = new WebSocket("ws://localhost:57839");
        webSocket.OnMessage += OnMessageReceived;
        webSocket.OnOpen += OnOpen;
        webSocket.OnClose += OnClose;
        webSocket.OnError += OnError;
        webSocket.ConnectAsync();
    }

    private void OnOpen(object sender, OpenEventArgs e)
    {
        Debug.Log("OnOpen   " + e.ToString());
    }

    private void OnClose(object sender, CloseEventArgs e)
    {
        Debug.LogError("OnClose   code: " + e.Code + "   reason: " + e.Reason);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        Debug.LogError("OnError   message: " + e.Message + "   exception: " + e.Exception);
    }

    private void OnMessageReceived(object sender, MessageEventArgs e)
    {
        Debug.Log("OnMessageReceived   " + e.Data);
        var msg = Msg.Parse(e.Data);
        switch (msg.Id)
        {
            case 1:
                {
                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new RTCIceCandidateConverter());
                    var candidate = JsonConvert.DeserializeObject<RTCIceCandidate>(msg.Content, settings);
                    PeerConnection?.AddIceCandidate(candidate);
                }
                break;
            case 3:
                {
                    var desc = JsonConvert.DeserializeObject<RTCSessionDescription>(msg.Content);
                    StartCoroutine(OnCreateAnswerSuccess(desc));
                }
                break;
        }
    }

    public void CloseWebSocket()
    {
        if (webSocket != null
            && (webSocket.ReadyState != WebSocketState.Closing || webSocket.ReadyState != WebSocketState.Closed))
        {
            webSocket.CloseAsync();
        }
    }

    #endregion

    #region Public

    /// <summary>
    /// 初始化PeerConnection
    /// </summary>
    public void InitP2P()
    {
        RTCConfiguration configuration = default;
        configuration.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };
        peerConnection = new RTCPeerConnection(ref configuration);
        peerConnection.OnIceCandidate = OnIceCandidate;
        peerConnection.OnIceConnectionChange = OnIceConnectionChange;
        peerConnection.OnNegotiationNeeded = OnNegotiationNeeded;
    }

    /// <summary>
    /// 设置Camera
    /// </summary>
    public void ResetCamera()
    {
        cameras = FindObjectsOfType<Camera>();
    }

    /// <summary>
    /// 重启P2P服务
    /// </summary>
    public void RestartP2P()
    {
        HangUp();
        RestartIce();
        CaptureStream();
    }

    /// <summary>
    /// 关闭P2P连接
    /// </summary>
    public void HangUp()
    {
        StopCoroutine(WebRTC.Update());
        StopAllCoroutines();

        if (peerConnection != null)
        {
            peerConnection.OnIceCandidate = null;
            peerConnection.OnIceConnectionChange = null;
            peerConnection.OnNegotiationNeeded = null;
        }

        RemoveTracks();

        videoStreams = null;

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        videoUpdateStarted = false;
    }


    #endregion
}
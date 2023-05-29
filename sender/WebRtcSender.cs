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

    [SerializeField] private float frameRatio = 30f; //���洫���֡��

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
    /// ��Ƶ��ע��ˢ��
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
    /// ʵʱ��ȡ��Ļ����
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
    /// AsyncGPUReadback�ص�
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
    /// �������� ICE
    /// </summary>
    private void RestartIce()
    {
        InitP2P();
        PeerConnection.RestartIce();
    }

    /// <summary>
    /// ����ICE��ѡ�¼�
    /// </summary>
    /// <param name="candidate"></param>
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new RTCIceCandidateConverter());
        var content = JsonConvert.SerializeObject(candidate, settings);
        var msg = new Msg(1, content);
        // ��WebSocket����ICE��ѡ
        webSocket.SendAsync(msg.ToString());
        Debug.Log("OnIceCandidate  " + msg.ToString());
    }

    /// <summary>
    /// ICE����״̬�仯����
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
    /// Э�̴����¼�
    /// </summary>
    private void OnNegotiationNeeded()
    {
        StartCoroutine(PeerNegotiationNeeded());
    }

    /// <summary>
    /// ����Offer�ɹ�
    /// </summary>
    /// <param name="desc"></param>
    /// <returns></returns>
    private IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"Offer from sender \n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        // ���ñ�������
        var op = PeerConnection.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            // ���ñ��������ɹ�
            OnSetLocalSuccess(PeerConnection);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        // ʹ��WebSocket���ʹ���Offer�ɹ���Ϣ�����ն�
        var msg = new Msg(2, desc);
        webSocket.SendAsync(msg.ToString());
    }

    /// <summary>
    /// ���ñ��������ɹ�
    /// </summary>
    /// <param name="pc"></param>
    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetLocalDescription complete");
    }

    /// <summary>
    /// ���ûỰ����������
    /// </summary>
    /// <param name="error"></param>
    private void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        HangUp();
    }

    /// <summary>
    /// ����Զ�������ɹ�
    /// </summary>
    /// <param name="pc"></param>
    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetRemoteDescription complete");
    }

    /// <summary>
    /// ����Ӧ��ɹ�����
    /// </summary>
    /// <param name="desc"></param>
    /// <returns></returns>
    private IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"setRemoteDescription start");

        //����Զ�̻Ự����
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
    /// �����Ự����������
    /// </summary>
    /// <param name="error"></param>
    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }

    /// <summary>
    /// ���״̬Э��
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
    /// Э��Э��
    /// </summary>
    /// <returns></returns>
    private IEnumerator PeerNegotiationNeeded()
    {
        // ����offer
        var op = PeerConnection.CreateOffer();
        yield return op;

        // ���û�д���
        if (!op.IsError)
        {
            // �������״̬���ȶ�
            if (PeerConnection.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"signaling state is not stable.");
                yield break;
            }

            // ����Offer�ɹ�����
            yield return StartCoroutine(OnCreateOfferSuccess(op.Desc));
        }
        else
        {
            // ����������󣬵��ô�������
            OnCreateSessionDescriptionError(op.Error);
        }
    }

    /// <summary>
    /// �Ƴ���Ƶ����
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
    /// ��ʼ��WebSocket
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
    /// ��ʼ��PeerConnection
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
    /// ����Camera
    /// </summary>
    public void ResetCamera()
    {
        cameras = FindObjectsOfType<Camera>();
    }

    /// <summary>
    /// ����P2P����
    /// </summary>
    public void RestartP2P()
    {
        HangUp();
        RestartIce();
        CaptureStream();
    }

    /// <summary>
    /// �ر�P2P����
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
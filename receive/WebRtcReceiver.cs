using UnityEngine;
using UnityEngine.UI;
using Unity.WebRTC;
using UnityWebSocket;
using System.Collections;
using System.Linq;
using Newtonsoft.Json;

public class WebRtcReceiver : MonoBehaviour
{
    #region Field

    // ��ʾ�����Unity UI RawImage
    [SerializeField] private RawImage display;
    // RTCPeerConnectionʵ��
    private RTCPeerConnection peerConnection;
    // WebSocketʵ��
    private WebSocket webSocket;
    // ����WebRTC��(MediaStream)
    private MediaStream receiveStream;
    // �Ƿ��Ѿ�������Ƶ����
    private bool videoUpdateStarted;

    #endregion

    #region LifeCycle

    // �ڶ����ʼ��ʱ����
    private void Awake()
    {
        display.enabled = false;

        // ��������ʼ��MediaStream����
        receiveStream = new MediaStream();

        // ����webrtc
        RTCConfiguration configuration = default;
        configuration.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };
        // ��������ʼ��RTCPeerConnection����
        peerConnection = new RTCPeerConnection(ref configuration);
        // ����RTCPeerConnection���¼��ص�
        peerConnection.OnIceCandidate = OnIceCandidate;
        peerConnection.OnIceConnectionChange = OnIceConnectionChange;
        peerConnection.OnTrack = OnTrack;

        // ����MediaStream���¼��ص�
        receiveStream.OnAddTrack = OnAddTrack;

        // �����Ƶ���»�δ��������ʼ��Ƶ����Э��
        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }

    // �ڿ�ʼ��Ϸʱ����
    private void Start()
    {
        // ��ʼ��WebSocket
        InitializeWebSocket();
    }

    // ���ٶ���ʱ����
    private void OnDestroy()
    {
        // �ر�WebRTC��WebSocket����
        HangUp();
    }


    #endregion

    #region WebRTC

    // ����������ӹ��ʱ�����Ļص�
    private void OnAddTrack(MediaStreamTrackEvent e)
    {
        if (e.Track is VideoStreamTrack track)
        {
            // �����յ���Ƶʱ����RawImage����������Ϊ��Ƶ����
            track.OnVideoReceived += tex =>
            {
                display.enabled = true;
                display.texture = tex;
                display.color = Color.white;
            };
        }
    }

    // ����Զ��ý���н��յ����ʱ�����Ļص�
    private void OnTrack(RTCTrackEvent e)
    {
        // ��������뵽��������
        receiveStream.AddTrack(e.Track);
    }

    // ��IceCandidate������ʱ�����Ļص�
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        // ��IceCandidate���л�ΪJSON
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new RTCIceCandidateConverter());
        var content = JsonConvert.SerializeObject(candidate, settings);
        var msg = new Msg(1, content);
        // ʹ��WebSocket����IceCandidate��������
        webSocket.SendAsync(msg.ToString());
        Debug.Log("OnIceCandidate  " + msg.ToString());
    }

    // ��Ice����״̬�����仯ʱ�����Ļص�
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"IceConnectionState: {state}");

        // ������״̬��Ϊ�����ӻ������ʱ�����ͳ����Ϣ
        if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
        {
            StartCoroutine(CheckStats());
        }
    }

    // ���ͳ����Ϣ��Э��
    IEnumerator CheckStats()
    {
        // �ӳ�0.1��
        yield return new WaitForSeconds(0.1f);
        if (peerConnection == null)
            yield break;

        // ��ȡͳ����Ϣ
        var op = peerConnection.GetStats();
        yield return op;
        if (op.IsError)
        {
            Debug.LogErrorFormat("RTCPeerConnection.GetStats failed: {0}", op.Error);
            yield break;
        }

        RTCStatsReport report = op.Value;
        RTCIceCandidatePairStats activeCandidatePairStats = null;
        RTCIceCandidateStats remoteCandidateStats = null;

        // ��ȡcandidate��Ϣ
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

        // ��ȡԶ�̺�ѡ�ߵ���Ϣ
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

    // ��Զ���������óɹ�ʱ��Э��
    private IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
    {
        Debug.Log($"setRemoteDescription start");
        var op2 = peerConnection.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(peerConnection);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        Debug.Log($"createAnswer start");

        var op3 = peerConnection.CreateAnswer();
        yield return op3;
        if (!op3.IsError)
        {
            yield return OnCreateAnswerSuccess(op3.Desc);
        }
        else
        {
            OnCreateSessionDescriptionError(op3.Error);
        }
    }

    // �ɹ����ñ���������Ļص�
    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetLocalDescription complete");
    }

    // ���ûỰ��������Ļص�
    void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        HangUp();
    }

    // �ɹ�����Զ��������Ļص�
    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetRemoteDescription complete");
    }

    // ������answer�����ɹ�ʱ��Э��
    IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
    {
        // �����ȡ��Answer��Ϣ
        Debug.Log($"Answer from :\n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        // ���ñ��ػỰ����
        var op = peerConnection.SetLocalDescription(ref desc);
        yield return op;

        // û�д���ʱ������OnSetLocalSuccess
        if (!op.IsError)
        {
            OnSetLocalSuccess(peerConnection);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        // ����WebSocket��Ϣ������Answer
        var msg = new Msg(3, desc);
        webSocket.SendAsync(msg.ToString());
    }

    // �����Ự��������ʱ�Ļص�����
    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }

    // �Ƴ����еĹ��
    private void RemoveTracks()
    {
        var tracks = receiveStream.GetTracks().ToArray();
        foreach (var track in tracks)
        {
            receiveStream.RemoveTrack(track);
        }
    }

    // �Ͽ�WebRTC���Ӳ��ر�WebSocket����
    private void HangUp()
    {
        RemoveTracks();

        peerConnection.Close();
        peerConnection.Dispose();
        peerConnection = null;

        // ����ʾ��RawImage����Ϊ��ɫ
        if (display != null)
        {
            display.enabled = false;
        }
        webSocket.CloseAsync();
    }

    #endregion

    #region WebSocket

    // ��ʼ��WebSocket����
    private void InitializeWebSocket()
    {
        webSocket = new WebSocket("ws://localhost:57839");
        // ΪWebSocket���¼��󶨴�����
        webSocket.OnMessage += OnMessageReceived;
        webSocket.OnOpen += OnOpen;
        webSocket.OnClose += OnClose;
        webSocket.OnError += OnError;
        webSocket.ConnectAsync();
    }

    // ��WebSocket���ӳɹ���ʱ�Ļص�
    private void OnOpen(object sender, OpenEventArgs e)
    {
        Debug.Log("OnOpen   " + e.ToString());
    }

    // ��WebSocket���ӹر�ʱ�Ļص�
    private void OnClose(object sender, CloseEventArgs e)
    {
        Debug.LogError("OnClose   code: " + e.Code + "   reason: " + e.Reason);
    }

    // ��WebSocket��������ʱ�Ļص�
    private void OnError(object sender, ErrorEventArgs e)
    {
        Debug.LogError("OnError   message: " + e.Message + "   exception: " + e.Exception);
    }

    // ���յ�WebSocket��Ϣʱ�Ĵ�����
    private void OnMessageReceived(object sender, MessageEventArgs e)
    {
        Debug.Log("OnMessageReceived   " + e.Data);
        // �����ӷ��������յ�����Ϣ
        var msg = Msg.Parse(e.Data);
        switch (msg.Id)
        {
            // �����յ���ICE��ѡ
            case 1:
                {
                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new RTCIceCandidateConverter());
                    var candidate = JsonConvert.DeserializeObject<RTCIceCandidate>(msg.Content, settings);
                    peerConnection.AddIceCandidate(candidate);
                }
                break;
            // �����յ���Զ������
            case 2:
                {
                    var desc = JsonConvert.DeserializeObject<RTCSessionDescription>(msg.Content);
                    StartCoroutine(OnCreateOfferSuccess(desc));
                }
                break;
        }
    }

    #endregion
}
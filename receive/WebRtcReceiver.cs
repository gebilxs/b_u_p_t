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

    // 显示画面的Unity UI RawImage
    [SerializeField] private RawImage display;
    // RTCPeerConnection实例
    private RTCPeerConnection peerConnection;
    // WebSocket实例
    private WebSocket webSocket;
    // 接收WebRTC流(MediaStream)
    private MediaStream receiveStream;
    // 是否已经启动视频更新
    private bool videoUpdateStarted;

    #endregion

    #region LifeCycle

    // 在对象初始化时调用
    private void Awake()
    {
        display.enabled = false;

        // 创建并初始化MediaStream对象
        receiveStream = new MediaStream();

        // 配置webrtc
        RTCConfiguration configuration = default;
        configuration.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };
        // 创建并初始化RTCPeerConnection对象
        peerConnection = new RTCPeerConnection(ref configuration);
        // 设置RTCPeerConnection的事件回调
        peerConnection.OnIceCandidate = OnIceCandidate;
        peerConnection.OnIceConnectionChange = OnIceConnectionChange;
        peerConnection.OnTrack = OnTrack;

        // 设置MediaStream的事件回调
        receiveStream.OnAddTrack = OnAddTrack;

        // 如果视频更新还未启动，开始视频更新协程
        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }

    // 在开始游戏时调用
    private void Start()
    {
        // 初始化WebSocket
        InitializeWebSocket();
    }

    // 销毁对象时调用
    private void OnDestroy()
    {
        // 关闭WebRTC和WebSocket连接
        HangUp();
    }


    #endregion

    #region WebRTC

    // 当往流中添加轨道时触发的回调
    private void OnAddTrack(MediaStreamTrackEvent e)
    {
        if (e.Track is VideoStreamTrack track)
        {
            // 当接收到视频时，将RawImage的纹理设置为视频画面
            track.OnVideoReceived += tex =>
            {
                display.enabled = true;
                display.texture = tex;
                display.color = Color.white;
            };
        }
    }

    // 当从远程媒体中接收到轨道时触发的回调
    private void OnTrack(RTCTrackEvent e)
    {
        // 将轨道加入到接收流中
        receiveStream.AddTrack(e.Track);
    }

    // 当IceCandidate被创建时触发的回调
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        // 将IceCandidate序列化为JSON
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new RTCIceCandidateConverter());
        var content = JsonConvert.SerializeObject(candidate, settings);
        var msg = new Msg(1, content);
        // 使用WebSocket发送IceCandidate到服务器
        webSocket.SendAsync(msg.ToString());
        Debug.Log("OnIceCandidate  " + msg.ToString());
    }

    // 当Ice连接状态发生变化时触发的回调
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"IceConnectionState: {state}");

        // 当连接状态变为已连接或已完成时，检查统计信息
        if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
        {
            StartCoroutine(CheckStats());
        }
    }

    // 检查统计信息的协程
    IEnumerator CheckStats()
    {
        // 延迟0.1秒
        yield return new WaitForSeconds(0.1f);
        if (peerConnection == null)
            yield break;

        // 获取统计信息
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

        // 获取candidate信息
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

        // 获取远程候选者的信息
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

    // 当远程描述设置成功时的协程
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

    // 成功设置本地描述后的回调
    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetLocalDescription complete");
    }

    // 设置会话描述错误的回调
    void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        HangUp();
    }

    // 成功设置远程描述后的回调
    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetRemoteDescription complete");
    }

    // 当创建answer操作成功时的协程
    IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
    {
        // 输出获取的Answer信息
        Debug.Log($"Answer from :\n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        // 设置本地会话描述
        var op = peerConnection.SetLocalDescription(ref desc);
        yield return op;

        // 没有错误时，调用OnSetLocalSuccess
        if (!op.IsError)
        {
            OnSetLocalSuccess(peerConnection);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        // 创建WebSocket消息并发送Answer
        var msg = new Msg(3, desc);
        webSocket.SendAsync(msg.ToString());
    }

    // 创建会话描述错误时的回调函数
    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }

    // 移除流中的轨道
    private void RemoveTracks()
    {
        var tracks = receiveStream.GetTracks().ToArray();
        foreach (var track in tracks)
        {
            receiveStream.RemoveTrack(track);
        }
    }

    // 断开WebRTC连接并关闭WebSocket连接
    private void HangUp()
    {
        RemoveTracks();

        peerConnection.Close();
        peerConnection.Dispose();
        peerConnection = null;

        // 将显示的RawImage设置为黑色
        if (display != null)
        {
            display.enabled = false;
        }
        webSocket.CloseAsync();
    }

    #endregion

    #region WebSocket

    // 初始化WebSocket连接
    private void InitializeWebSocket()
    {
        webSocket = new WebSocket("ws://localhost:57839");
        // 为WebSocket的事件绑定处理方法
        webSocket.OnMessage += OnMessageReceived;
        webSocket.OnOpen += OnOpen;
        webSocket.OnClose += OnClose;
        webSocket.OnError += OnError;
        webSocket.ConnectAsync();
    }

    // 当WebSocket连接成功打开时的回调
    private void OnOpen(object sender, OpenEventArgs e)
    {
        Debug.Log("OnOpen   " + e.ToString());
    }

    // 当WebSocket连接关闭时的回调
    private void OnClose(object sender, CloseEventArgs e)
    {
        Debug.LogError("OnClose   code: " + e.Code + "   reason: " + e.Reason);
    }

    // 当WebSocket发生错误时的回调
    private void OnError(object sender, ErrorEventArgs e)
    {
        Debug.LogError("OnError   message: " + e.Message + "   exception: " + e.Exception);
    }

    // 当收到WebSocket消息时的处理方法
    private void OnMessageReceived(object sender, MessageEventArgs e)
    {
        Debug.Log("OnMessageReceived   " + e.Data);
        // 解析从服务器接收到的消息
        var msg = Msg.Parse(e.Data);
        switch (msg.Id)
        {
            // 处理收到的ICE候选
            case 1:
                {
                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new RTCIceCandidateConverter());
                    var candidate = JsonConvert.DeserializeObject<RTCIceCandidate>(msg.Content, settings);
                    peerConnection.AddIceCandidate(candidate);
                }
                break;
            // 处理收到的远程描述
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
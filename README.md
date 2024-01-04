# BUPT保研测试项目-通过webRTC实现unity两个游戏的屏幕捕捉


要实现这个功能，您需要使用Unity的WebRTC Package，在一个游戏中捕捉画面并将其传输到另一个游戏中。以下是实现该功能的大致步骤：

1. 在两个Unity项目中分别安装WebRTC Package： 打开Unity Package Manager (Window -> Package Manager)，然后搜索并安装"WebRTC"。

2. 在两个项目中创建一个新的GameObject，然后为其添加一个C#脚本，分别命名为"Game1WebRTC"和"Game2WebRTC"。

3. 编辑Game1WebRTC脚本，实现以下功能：

   a. 创建一个WebRTC连接。 b. 使用Unity的Camera类捕捉游戏画面。 c. 将捕获的画面编码为视频流。 d. 使用WebRTC连接将视频流发送到另一个游戏。

   可参考WebRTC官方文档和示例来实现上述功能：https://github.com/Unity-Technologies/com.unity.webrtc

4. 编辑Game2WebRTC脚本，实现以下功能：

   a. 创建一个WebRTC连接，与Game1WebRTC的连接匹配。 b. 接收来自Game1WebRTC的视频流。 c. 解码视频流。 d. 在游戏中创建一个新的RenderTexture，将解码后的视频流渲染到该纹理上。 e. 在游戏中创建一个新的Quad或Plane对象，将其材质的主纹理设置为上述RenderTexture，以显示Game1的画面。

5. 配置两个游戏之间的网络连接，以便它们可以相互通信。这可能需要您搭建一个简单的信令服务器，用于协调WebRTC连接的建立。您可以使用Node.js、Socket.IO等工具来实现这个功能。

6. 分别构建和运行两个游戏，测试是否可以正常捕获和显示画面。请注意，在运行游戏时可能需要处理防火墙和网络设置，以确保数据包可以正确传输。

7. 如果一切正常，您应该可以在Game2中看到Game1的实时画面。注意这种实现可能会引入一定的延迟，并受到网络速度和画面质量的影响。根据需要优化设置以获得最佳性能。



## 详细步骤流程

1. 安装WebRTC Package：

   a. 打开两个Unity项目，分别打开Window -> Package Manager。

   b. 在Package Manager中点击左上角的 "+" 按钮，选择 "Add package from git URL..."。

   c. 输入以下URL并点击 "Add"：`https://github.com/Unity-Technologies/com.unity.webrtc.git`。Unity将自动安装WebRTC插件包。

2. 创建新的GameObject和脚本：

   a. 在两个项目的场景中，分别点击右键选择 "Create Empty"，创建一个空的GameObject。

   b. 选中新创建的GameObject，在 "Inspector" 面板中点击 "Add Component" 按钮，选择 "New Script"，分别为两个项目的脚本命名为 "Game1WebRTC" 和 "Game2WebRTC"。

3. 编辑Game1WebRTC脚本：

   a. 创建一个WebRTC连接：

   - 首先，需要实例化一个`RTCPeerConnection`对象。这是WebRTC连接的核心类，负责协调连接和数据传输。
   - 实例化时，传入`RTCConfiguration`对象以配置连接，包括ICE服务器（用于穿越NAT）等信息。

   b. 使用Unity的Camera类捕捉游戏画面：

   - 在脚本中定义一个`Camera`类型的变量，将需要捕获画面的摄像机分配给这个变量。
   - 创建一个`RenderTexture`对象，并将其分配给摄像机的`targetTexture`属性，这样摄像机的画面将被渲染到该纹理上。

   c. 将捕获的画面编码为视频流：

   - 创建一个`VideoTrack`对象，使用`RTCPeerConnection`的`AddTrack`方法将其添加到连接中。
   - 使用`RenderTexture`对象作为输入，将游戏画面编码为视频流，并关联到`VideoTrack`。

   d. 使用WebRTC连接将视频流发送到另一个游戏：

   - 创建一个简单的信令服务器（例如使用Node.js和Socket.IO），以便两个游戏之间可以交换连接信息，建立WebRTC连接。
   - 将`RTCPeerConnection`的本地描述信息发送给信令服务器，从而将其传递给另一个游戏。
   - 接收另一个游戏返回的远程描述信息，并将其设置给`RTCPeerConnection`，以建立连接。

4. 编辑Game2WebRTC脚本：

   a. 创建一个WebRTC连接：

   - 同样，实例化一个`RTCPeerConnection`对象，传入相同的`RTCConfiguration`对象。

   b. 接收来自Game1WebRTC的视频流：

   - 通过信令服务器，接收来自Game1WebRTC的连接信息，并将其设置为`RTCPeerConnection`的远程描述。

   - 使用`OnTrack`事件监听器，监听新的视频流，这

   - 个事件在新的视频流被添加到连接时触发。

     c. 解码视频流： - 当`OnTrack`事件触发时，获取传入的`RTCTrackEvent`对象，从中提取视频流（`VideoStreamTrack`）。 - `VideoStreamTrack`对象会自动处理视频流的解码。

     d. 在游戏中创建一个新的RenderTexture，将解码后的视频流渲染到该纹理上： - 创建一个新的`RenderTexture`对象，分辨率和格式需要与发送方的RenderTexture相匹配。 - 将解码后的视频流（`VideoStreamTrack`）渲染到新创建的`RenderTexture`上。

     e. 在游戏中创建一个新的Quad或Plane对象，将其材质的主纹理设置为上述RenderTexture，以显示Game1的画面： - 在场景中创建一个新的3D对象，例如Quad或Plane。 - 创建一个新的材质，并将其分配给新创建的3D对象。 - 将新创建的RenderTexture设置为该材质的主纹理，这将使Game1的画面渲染到3D对象上。

     

5. 配置两个游戏之间的网络连接：

   a. 创建一个简单的信令服务器，例如使用Node.js和Socket.IO。信令服务器用于协调WebRTC连接的建立，让两个游戏交换连接信息。

   b. 在Game1和Game2的脚本中分别实现与信令服务器的连接和数据交换。例如，Game1将本地描述信息发送给信令服务器，然后信令服务器将其转发给Game2。类似地，Game2也将远程描述信息发送回信令服务器，再由信令服务器转发给Game1。

   c. 在两个脚本中处理信令服务器发来的数据，更新`RTCPeerConnection`的本地和远程描述信息，从而建立WebRTC连接。

6. 分别构建和运行两个游戏，测试是否可以正常捕获和显示画面：

   a. 在Unity编辑器中，分别为两个项目设置构建平台（如Windows或其他支持的平台）。

   b. 点击File -> Build Settings，为两个项目构建可执行文件。

   c. 运行构建好的两个游戏，观察是否能正常捕获和显示画面。可能需要处理防火墙和网络设置，以确保数据包可以正确传输。

7. 优化和调整：

   a. 观察游戏运行时的延迟和画面质量。根据需要调整WebRTC连接、视频编码和RenderTexture的设置，以获得最佳性能。

   b. 如果遇到性能问题，可以尝试降低视频分辨率、降低帧率或使用更高效的视频编码设置

   c. 如果遇到网络问题，可以检查防火墙和端口设置，确保两个游戏之间的数据包可以正确传输。还可以考虑在`RTCConfiguration`中设置不同的ICE服务器以改善连接质量。

8. 扩展功能和应用：

   a. 根据项目需求，可以在Game1和Game2之间添加双向通信，例如允许Game2控制Game1中的某些对象。

   b. 如果您想要实现更复杂的场景，如多人游戏，可以使用类似的方法，让多个客户端之间建立WebRTC连接，并在游戏中共享视频和音频流。

   c. 除了实时游戏画面外，还可以使用WebRTC在游戏中共享其他类型的媒体，如音频流、数据通道等。这可以帮助实现实时语音聊天、数据同步等功能。

## 使用unityhub进行管理

下载失败后发现使用unityhub可以很好的管理

Unity Hub是最新推出的用于简化工作流程的桌面端应用程序。它提供了一个用于管理Unity项目、简化下载、查找，卸载以及安装管理多个Unity版本的工具。

###　更改下载位置

![image-20230424231138081](https://img-gebilxs-1309460599.cos.ap-shanghai.myqcloud.com/img/image-20230424231138081.png)![image-20230424231149484](https://img-gebilxs-1309460599.cos.ap-shanghai.myqcloud.com/img/image-20230424231149484.png)



遇到闪退

以管理员身份打开

替代方案 打开本地游戏



按照官网方式进行安装

https://github.com/Unity-Technologies/com.unity.webrtc

https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/tutorial.html





关于执行：

Unity项目中的C#脚本不会在项目开启时自动执行。脚本必须附加到一个场景中的游戏对象（GameObject）上，当场景被加载时，附加到游戏对象的脚本才会执行。

具体来说，以下情况下的脚本将执行：

1. 当脚本被添加到场景中的一个游戏对象上时。
2. 当脚本中的某个方法被特定的Unity事件触发时，例如`Start()`、`Update()`、`Awake()`等。这些方法在不同的生命周期阶段被调用，详情可以参考[Unity文档](https://docs.unity3d.com/Manual/ExecutionOrder.html)。

请注意，如果一个脚本不附加到任何游戏对象上，或者所附加的游戏对象未启用（disabled），那么脚本将不会执行。

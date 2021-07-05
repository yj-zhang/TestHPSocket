using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HPSocket.Tcp;
using HPSocket;
using Newtonsoft.Json;

namespace TestHPSocket
{
    public partial class Form1 : Form
    {
        readonly ITcpPullAgent _agent = new TcpPullAgent();
        private const int MaxPacketSize = 4096;
        delegate void AddLogHandler(string log);
        IntPtr[] connIds = new IntPtr[2];
        public Form1()
        {
            InitializeComponent();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            AddLog($"connId = {connIds[0]}  connectState = {_agent.GetConnectionState(connIds[0])}");
            AddLog($"connId = {connIds[1]}  connectState = {_agent.GetConnectionState(connIds[1])}");

        }

        private void AddLog(string log)
        {
            if (txtLog.IsDisposed)
            {
                return;
            }

            // 从ui线程去操作ui
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new AddLogHandler(AddLog), log);
            }
            else
            {
                txtLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {log}\r\n");
            }
        }

        private void AddLog1(string log)
        {
            if (textBox1.IsDisposed)
            {
                return;
            }

            // 从ui线程去操作ui
            if (textBox1.InvokeRequired)
            {
                textBox1.Invoke(new AddLogHandler(AddLog1), log);
            }
            else
            {
                textBox1.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {log}\r\n");
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 缓冲区大小
            _agent.SocketBufferSize = 4096; // 4K
            // 异步连接
            _agent.Async = true;
            // 异步连接可以设置连接超时时间, 单位是毫秒
            _agent.ConnectionTimeout = 3000;

            // 注意这里是监听地址, 连接服务器的ip和端口在调用Connect()的时候传入
            _agent.Address = "0.0.0.0";

            _agent.OnPrepareConnect += _agent_OnPrepareConnect;
            _agent.OnConnect += _agent_OnConnect;
            _agent.OnReceive += _agent_OnReceive;
            _agent.OnClose += _agent_OnClose;
        }

        private HandleResult _agent_OnClose(IAgent sender, IntPtr connId, SocketOperation socketOperation, int errorCode)
        {
            AddLog1($"OnClose({connId}), socket operation: {socketOperation}, error code: {errorCode}");
            return HandleResult.Ok;
        }

        private HandleResult _agent_OnReceive(IAgent sender, IntPtr connId, int length)
        {
            if (!(sender is ITcpPullAgent agent))
            {
                return HandleResult.Error;
            }

            // 封包头长度
            const int headerLength = sizeof(int);
            HandleResult result;
            do
            {
                // 窥探缓冲区, 取头部字节得到包头
                var fr = agent.Peek(connId, headerLength, out var packetHeader);

                // 连接已断开
                if (fr == FetchResult.DataNotFound)
                {
                    result = HandleResult.Error;
                    break;
                }

                // 如果来的数据长度不够封包头的长度, 等下次在处理 
                if (fr == FetchResult.LengthTooLong)
                {
                    result = HandleResult.Ignore;
                    break;
                }

                // 两端字节序要保持一致
                // 如果当前环境不是小端字节序
                if (!BitConverter.IsLittleEndian)
                {
                    // 翻转字节数组, 变为小端字节序
                    Array.Reverse(packetHeader);
                }

                // 得到包头指向的数据长度
                var dataLength = BitConverter.ToInt32(packetHeader, 0);

                // 完整的包长度(含包头和完整数据的大小)
                var fullLength = dataLength + headerLength;

                if (dataLength < 0 || fullLength > MaxPacketSize)
                {
                    result = HandleResult.Error;
                    break;
                }

                // 如果缓冲区数据长度不够一个完整的包, 当前不处理
                if (length < fullLength)
                {
                    result = HandleResult.Ignore;
                    break;
                }

                // 从缓冲区取数据, 注意取的是 fullLength 长的包
                fr = agent.Fetch(connId, fullLength, out var data);

                // 连接已断开
                if (fr == FetchResult.DataNotFound)
                {
                    result = HandleResult.Error;
                    break;
                }

                // 如果来的数据长度不够封包头的长度, 等下次在处理 
                if (fr == FetchResult.LengthTooLong)
                {
                    result = HandleResult.Ignore;
                    break;
                }

                // 逻辑上fr到这里必然ok

                // 注意: 现在data里是包含包头的
                // 到这里当前只处理这一个包, 其他的数据不fetch, 等下次OnReceive到达处理
                result = OnProcessFullPacket(sender, connId, data, headerLength);
                if (result == HandleResult.Error)
                {
                    break;
                }

                // 继续下一次循环

            } while (true);

            return result;
        }

        private HandleResult OnProcessFullPacket(IAgent sender, IntPtr connId, byte[] data, int headerLength)
        {
            // 这里来的都是完整的包
            // 但是因为数据是包含包头的, 所以转字符串的时候注意 Encoding.UTF8.GetString() 的用法
            var packet = JsonConvert.DeserializeObject<Packet>(Encoding.UTF8.GetString(data, headerLength, data.Length - headerLength));
            var result = HandleResult.Ok;
            switch (packet.Type)
            {
                case PacketType.Echo: // 回显是个字符串显示操作
                    {
                        AddLog1($"OnProcessFullPacket(), type: {packet.Type}, content: {packet.Data}");
                        break;
                    }
                case PacketType.Time: // 获取服务器时间依然是个字符串操作^_^
                    {
                        AddLog1($"OnProcessFullPacket(), type: {packet.Type}, time: {packet.Data}");
                        break;
                    }
                default:
                    result = HandleResult.Error;
                    break;
            }
            return result;
        }

        private HandleResult _agent_OnConnect(IAgent sender, IntPtr connId, IProxy proxy)
        {
            return HandleResult.Ok;
        }

        private HandleResult _agent_OnPrepareConnect(IAgent sender, IntPtr connId, IntPtr socket)
        {
            return HandleResult.Ok;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_agent.HasStarted)
            {
                MessageBox.Show(@"请先断开与服务器的连接", @"正在通信:", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }

            // 停止并释放客户端
            _agent.Dispose();

            e.Cancel = false;
        }


        /// <summary>
        /// 发送包头
        /// <para>固定包头网络字节序</para>
        /// </summary>
        /// <param name="sender">服务器组件</param>
        /// <param name="connId">连接id</param>
        /// <param name="dataLength">实际数据长度</param>
        /// <returns></returns>
        private bool SendPacketHeader(IAgent sender, IntPtr connId, int dataLength)
        {
            // 包头转字节数组
            var packetHeaderBytes = BitConverter.GetBytes(dataLength);

            // 两端字节序要保持一致
            // 如果当前环境不是小端字节序
            if (!BitConverter.IsLittleEndian)
            {
                // 翻转字节数组, 变为小端字节序
                Array.Reverse(packetHeaderBytes);
            }

            return sender.Send(connId, packetHeaderBytes, packetHeaderBytes.Length);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!_agent.Start())
            {
                throw new Exception($"Start() error code: {_agent.ErrorCode}, error message: {_agent.ErrorMessage}");
            }
            for (int i = 0; i < 2; i++)
            {
                if (!_agent.Connect("127.0.0.1", 5555, out connIds[i]))
                {
                    throw new Exception($"error code: {_agent.ErrorCode}, error message: {_agent.ErrorMessage}");
                }
            }
            
            timer1.Enabled = true;
        }


        /// <summary>
        /// 封包
        /// </summary>
        public class Packet
        {
            /// <summary>
            /// 封包类型
            /// </summary>
            public PacketType Type { get; set; }

            /// <summary>
            /// 数据
            /// </summary>
            public string Data { get; set; }
        }

        /// <summary>
        /// 封包类型
        /// </summary>
        public enum PacketType
        {
            /// <summary>
            /// 回显
            /// </summary>
            Echo = 1,
            /// <summary>
            /// 时间
            /// </summary>
            Time
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="connId"></param>
        /// <param name="type"></param>
        /// <param name="data"></param>
        private void Send(IAgent sender, IntPtr connId, PacketType type, string data)
        {
            if (!_agent.HasStarted)
            {
                return;
            }

            // 组织封包, 取得要发送的数据
            var packet = new Packet
            {
                Type = type,
                Data = data,
            };

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));

            // 先发包头
            if (!SendPacketHeader(sender, connId, bytes.Length))
            {
                _agent.Disconnect(connId);
                return;
            }

            // 再发实际数据
            if (!_agent.Send(connId, bytes, bytes.Length))
            {
                _agent.Disconnect(connId);
            }
        }


        private async void button3_Click(object sender, EventArgs e)
        {
            await _agent.StopAsync();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            var connIds = _agent.GetAllConnectionIds();
            foreach (var connId in connIds)
            {
                Send(_agent, connId, PacketType.Echo, txtContent.Text.Trim());
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (button4.Text.Equals("停止状态查询"))
            {
                timer1.Enabled = false;
                button4.Text = "启动状态查询";
            }
            else if (button4.Text.Equals("启动状态查询"))
            {
                timer1.Enabled = true;
                button4.Text = "停止状态查询";
            }
        }
    }
}

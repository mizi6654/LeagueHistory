using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using League.model;
using Newtonsoft.Json.Linq;

namespace League
{
    public partial class Search : Form
    {
        private readonly SgpSession _sgpSession = new SgpSession();

        public Search()
        {
            InitializeComponent();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            string name = "不准杀猫猫#79272";
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            //var client = new HttpClient(handler);

            //// 关键：必须加 UA，否则部分 QQ 服务器会拒绝连接
            //client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");

            //client.DefaultRequestVersion = HttpVersion.Version11; // 强制使用 HTTP/1.1，防止 HTTP/2 问题
            //client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            //string url = $"https://acs.game.qq.com/pvp/proxy/api/getMatchList?start=0&count=20&summonerName={Uri.EscapeDataString(name)}";

            //HttpClientHandler handler = new HttpClientHandler
            //{
            //    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            //    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            //};

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            string url = $"https://acs.game.qq.com/pvp/proxy/api/getMatchList?start=0&count=20&summonerName={Uri.EscapeDataString(name)}";
            var res = await client.GetAsync(url);
            string content = await res.Content.ReadAsStringAsync();
            Console.WriteLine(content);

            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("请求失败：" + ex.Message);
            }
            //string json = GetMatchList(name);
            //ParseAndShow(json, txtResult);
            //Debug.WriteLine(txtResult);
        }

        public static string GetMatchList(string summonerName, int count = 20)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string url = $"https://acs.game.qq.com/pvp/proxy/api/getMatchList?start=0&count={count}&summonerName={Uri.EscapeDataString(summonerName)}";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0"; // 有些接口需要 UA
                req.Method = "GET";
                req.Timeout = 5000;

                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(res.GetResponseStream()))
                {
                    string json = reader.ReadToEnd();
                    Debug.WriteLine($"{json}");
                    return json;
                }
            }
            catch (Exception ex)
            {
                return $"请求失败：{ex.Message}";
            }
        }

        public static void ParseAndShow(string json, TextBox txt)
        {
            try
            {
                JObject root = JObject.Parse(json);
                var games = root["data"]["game_list"];
                txt.Clear();
                foreach (var game in games)
                {
                    string champ = game["champion_name"].ToString();
                    string kda = $"{game["kills"]}/{game["deaths"]}/{game["assists"]}";
                    long timestamp = long.Parse(game["timestamp"].ToString());
                    DateTime dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
                    txt.AppendText($"{dt:MM-dd HH:mm} - {champ} - KDA: {kda}\r\n");
                }
            }
            catch (Exception ex)
            {
                txt.Text = "解析失败: " + ex.Message;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            LOLHelper helper = new LOLHelper();

            string exePath = helper.GetLOLLoginExePath();
            tbPath.Text = exePath;

            if (!string.IsNullOrEmpty(exePath))
            {
                Console.WriteLine("找到 LOL 登录程序：" + exePath);

                // 启动
                helper.StartLOLLoginProgram(exePath);
            }
            else
            {
                Console.WriteLine("未检测到 LOL 登录程序！");
            }
        }

        private async void btnFetchMatches_Click(object sender, EventArgs e)
        {
            
        }
    }
}

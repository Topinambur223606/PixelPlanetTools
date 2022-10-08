using PixelPlanetUtils;
using PixelPlanetUtils.Logging;
using PixelPlanetUtils.NetworkInteraction;
using PixelPlanetUtils.NetworkInteraction.Websocket;
using Svg;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PixelPlanetBot.Captcha
{
    partial class CaptchaForm : Form
    {
        private static bool enabledVisualStyles;
        
        public static void EnableVisualStyles()
        {
            if (!enabledVisualStyles)
            {
                enabledVisualStyles = true;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
        }

        private readonly PixelPlanetHttpApi api;
        private readonly WebsocketWrapper websocket;
        private readonly Logger logger;
        private string captchaId;

        public bool ShowInBackground { get; set; }

        protected override bool ShowWithoutActivation => ShowInBackground;

        public CaptchaForm(ProxySettings proxySettings, WebsocketWrapper websocket, Logger logger)
        {
            InitializeComponent();
            api = new PixelPlanetHttpApi
            {
                ProxySettings = proxySettings
            };
            this.websocket = websocket;
            this.logger = logger;
        }

        private async void FormLoad(object sender, EventArgs e)
        {
            if (ShowInBackground)
            {
                User32.SendWindowToBackground(Handle);
            }
            await SetCaptchaImage();
        }

        private async Task SetCaptchaImage()
        {
            (captchaId, SvgDocument svgImage) = await api.GetCaptchaImageAsync();
            captchaPictureBox.Image?.Dispose();
            captchaPictureBox.Image = svgImage.Draw();
        }

        private async void GetButtonClick(object sender, EventArgs e)
        {
            await SetCaptchaImage();
        }

        private async void SendButtonClick(object sender, EventArgs e)
        {
            try
            {
                websocket.SendCaptchaResponse(captchaId, solutionTextBox.Text);
                var returnCode = websocket.GetCaptchaResponse();
                logger.LogCaptchaResult(returnCode);
                if (returnCode == CaptchaReturnCode.Success)
                {
                    Close();
                }
                else
                {
                    throw new Exception(ErrorTextResources.Get(returnCode));
                }
            }
            catch (Exception ex)
            {
                solutionTextBox.Clear();
                var setTask = SetCaptchaImage();
                MessageBox.Show(ex.Message, "Error");
                await setTask;
            }
        }
    }
}

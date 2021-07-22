using PixelPlanetUtils.NetworkInteraction;
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

        public bool ShowInBackground { get; set; }

        protected override bool ShowWithoutActivation => ShowInBackground;

        public CaptchaForm(ProxySettings proxySettings)
        {
            InitializeComponent();
            api = new PixelPlanetHttpApi
            {
                ProxySettings = proxySettings
            };
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
            SvgDocument svgImage = await api.GetCaptchaImageAsync();
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
                await api.PostCaptchaText(solutionTextBox.Text);
                Close();
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

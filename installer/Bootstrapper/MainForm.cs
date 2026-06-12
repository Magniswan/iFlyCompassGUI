using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace iFlyCompassGUI.Bootstrapper
{
    public partial class MainForm : Form
    {
        private readonly InstallerEngine _engine;
        private CancellationTokenSource? _cts;
        private bool _completed;

        public MainForm()
        {
            InitializeComponent();
            _engine = new InstallerEngine();
            _engine.StepChanged += OnStepChanged;
            _engine.ProgressChanged += OnProgressChanged;
            _engine.InstallCompleted += OnInstallCompleted;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartInstallation();
        }

        private void StartInstallation()
        {
            _cts = new CancellationTokenSource();
            actionButton.Text = "取消";
            actionButton.Enabled = true;

            var _ = RunInstallAsync();
        }

        private async Task RunInstallAsync()
        {
            try
            {
                await _engine.InstallAsync(_cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                OnInstallCompleted(this, new InstallCompletedEventArgs(false, ex.Message));
            }
        }

        private void OnStepChanged(object sender, StepChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnStepChanged(sender, e)));
                return;
            }

            statusLabel.Text = e.StepDescription;
            if (e.OverallProgress >= 0 && e.OverallProgress <= 100)
                progressBar.Value = e.OverallProgress;
        }

        private void OnProgressChanged(object sender, ProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnProgressChanged(sender, e)));
                return;
            }

            detailLabel.Text = e.Detail;
            if (e.Percent >= 0 && e.Percent <= 100)
                progressBar.Value = e.Percent;
        }

        private void OnInstallCompleted(object sender, InstallCompletedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnInstallCompleted(sender, e)));
                return;
            }

            _completed = true;

            if (e.Success)
            {
                statusLabel.Text = "安装完成！";
                detailLabel.Text = "iFlyCompassGUI 已成功安装，您可以从开始菜单启动它。";
                progressBar.Value = 100;
                actionButton.Text = "关闭";
                actionButton.Enabled = true;
            }
            else
            {
                statusLabel.Text = "安装失败";
                detailLabel.Text = e.ErrorMessage;
                actionButton.Text = "关闭";
                actionButton.Enabled = true;
            }
        }

        private void actionButton_Click(object sender, EventArgs e)
        {
            if (_engine.IsInstalling && !_completed)
            {
                // 取消安装
                _cts?.Cancel();
                actionButton.Enabled = false;
                actionButton.Text = "正在取消...";
            }
            else
            {
                Close();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_engine.IsInstalling && !_completed)
            {
                var result = MessageBox.Show(
                    "安装尚未完成，确定要退出吗？",
                    "确认退出",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _cts?.Cancel();
            }
        }
    }
}

using System;
using System.Windows.Forms;
using SshTabClient.Models;
using SshTabClient.Services;

namespace SshTabClient.Forms
{
    public class ServerEditForm : Form
    {
        public ServerProfile Result { get; }

        private readonly TextBox _nameBox;
        private readonly TextBox _hostBox;
        private readonly NumericUpDown _portBox;
        private readonly TextBox _userBox;
        private readonly RadioButton _pwdRadio;
        private readonly RadioButton _keyRadio;
        private readonly TextBox _pwdBox;
        private readonly TextBox _keyPathBox;
        private readonly TextBox _passphraseBox;
        private readonly Panel _pwdPanel;
        private readonly Panel _keyPanel;

        private readonly string? _existingEncryptedPassword;
        private readonly string? _existingEncryptedPassphrase;

        private const int LabelLeft = 16;
        private const int FieldLeft = 108;
        private const int FieldWidth = 240;
        private const int RowHeight = 28;

        public ServerEditForm(ServerProfile? existing = null)
        {
            Text = existing == null ? "서버 추가" : "서버 수정";
            Width = 392;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;

            Result = existing ?? new ServerProfile();
            _existingEncryptedPassword = existing?.EncryptedPassword;
            _existingEncryptedPassphrase = existing?.EncryptedPassphrase;

            int y = 16;

            var nameLabel = new Label { Text = "이름", Left = LabelLeft, Top = y + 3, AutoSize = true };
            _nameBox = new TextBox { Left = FieldLeft, Top = y, Width = FieldWidth, Text = Result.Name };
            y += RowHeight;

            var hostLabel = new Label { Text = "호스트/IP", Left = LabelLeft, Top = y + 3, AutoSize = true };
            _hostBox = new TextBox { Left = FieldLeft, Top = y, Width = FieldWidth, Text = Result.Host };
            y += RowHeight;

            var portLabel = new Label { Text = "포트", Left = LabelLeft, Top = y + 3, AutoSize = true };
            _portBox = new NumericUpDown
            {
                Left = FieldLeft, Top = y, Width = 90, Minimum = 1, Maximum = 65535,
                Value = Result.Port <= 0 ? 22 : Result.Port
            };
            y += RowHeight;

            var userLabel = new Label { Text = "계정", Left = LabelLeft, Top = y + 3, AutoSize = true };
            _userBox = new TextBox { Left = FieldLeft, Top = y, Width = FieldWidth, Text = Result.Username };
            y += RowHeight;

            var authLabel = new Label { Text = "인증 방식", Left = LabelLeft, Top = y + 3, AutoSize = true };
            _pwdRadio = new RadioButton { Text = "비밀번호", Left = FieldLeft, Top = y, AutoSize = true };
            _keyRadio = new RadioButton { Text = "SSH 키 파일", Left = FieldLeft + 100, Top = y, AutoSize = true };
            y += RowHeight;

            _pwdPanel = new Panel { Left = FieldLeft, Top = y, Width = FieldWidth, Height = 24 };
            _pwdBox = new TextBox { Left = 0, Top = 0, Width = FieldWidth, UseSystemPasswordChar = true };
            _pwdPanel.Controls.Add(_pwdBox);

            _keyPanel = new Panel { Left = FieldLeft, Top = y, Width = FieldWidth, Height = 62, Visible = false };
            _keyPathBox = new TextBox { Left = 0, Top = 0, Width = FieldWidth - 78 };
            var browseBtn = new Button { Text = "찾아보기", Left = FieldWidth - 74, Top = -3, Width = 74, Height = 28 };
            browseBtn.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Title = "개인키 파일 선택" };
                if (ofd.ShowDialog(this) == DialogResult.OK) _keyPathBox.Text = ofd.FileName;
            };
            var passLabel = new Label { Text = "키 암호(선택)", Left = 0, Top = 32, AutoSize = true };
            _passphraseBox = new TextBox { Left = 92, Top = 28, Width = FieldWidth - 92, UseSystemPasswordChar = true };
            _keyPanel.Controls.Add(_keyPathBox);
            _keyPanel.Controls.Add(browseBtn);
            _keyPanel.Controls.Add(passLabel);
            _keyPanel.Controls.Add(_passphraseBox);

            y += 70;

            var okBtn = new Button { Text = "저장", Left = FieldLeft + FieldWidth - 172, Top = y, Width = 82, Height = 30, DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = "취소", Left = FieldLeft + FieldWidth - 82, Top = y, Width = 82, Height = 30, DialogResult = DialogResult.Cancel };
            okBtn.Click += OkBtn_Click;

            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            _pwdRadio.CheckedChanged += (s, e) => { _pwdPanel.Visible = _pwdRadio.Checked; _keyPanel.Visible = !_pwdRadio.Checked; };
            _keyRadio.CheckedChanged += (s, e) => { _keyPanel.Visible = _keyRadio.Checked; _pwdPanel.Visible = !_keyRadio.Checked; };

            if (Result.AuthType == AuthType.KeyFile)
            {
                _keyRadio.Checked = true;
                _keyPathBox.Text = Result.KeyFilePath ?? "";
            }
            else
            {
                _pwdRadio.Checked = true;
            }

            Height = y + 96;

            Controls.Add(nameLabel); Controls.Add(_nameBox);
            Controls.Add(hostLabel); Controls.Add(_hostBox);
            Controls.Add(portLabel); Controls.Add(_portBox);
            Controls.Add(userLabel); Controls.Add(_userBox);
            Controls.Add(authLabel); Controls.Add(_pwdRadio); Controls.Add(_keyRadio);
            Controls.Add(_pwdPanel); Controls.Add(_keyPanel);
            Controls.Add(okBtn); Controls.Add(cancelBtn);
        }

        private void OkBtn_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text) || string.IsNullOrWhiteSpace(_hostBox.Text) ||
                string.IsNullOrWhiteSpace(_userBox.Text))
            {
                MessageBox.Show(this, "이름, 호스트, 계정은 필수입니다.", "확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            Result.Name = _nameBox.Text.Trim();
            Result.Host = _hostBox.Text.Trim();
            Result.Port = (int)_portBox.Value;
            Result.Username = _userBox.Text.Trim();

            if (_pwdRadio.Checked)
            {
                Result.AuthType = AuthType.Password;
                Result.EncryptedPassword = string.IsNullOrEmpty(_pwdBox.Text)
                    ? _existingEncryptedPassword
                    : CredentialProtector.Protect(_pwdBox.Text);
                Result.KeyFilePath = null;
                Result.EncryptedPassphrase = null;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_keyPathBox.Text))
                {
                    MessageBox.Show(this, "개인키 파일을 선택하세요.", "확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                Result.AuthType = AuthType.KeyFile;
                Result.KeyFilePath = _keyPathBox.Text.Trim();
                Result.EncryptedPassphrase = string.IsNullOrEmpty(_passphraseBox.Text)
                    ? _existingEncryptedPassphrase
                    : CredentialProtector.Protect(_passphraseBox.Text);
                Result.EncryptedPassword = null;
            }
        }
    }
}

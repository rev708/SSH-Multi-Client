using System.Collections.Generic;
using System.Windows.Forms;
using SshTabClient.Models;
using SshTabClient.Services;

namespace SshTabClient.Forms
{
    public class ServerManageForm : Form
    {
        private readonly ProfileStore _store;
        private readonly ListBox _list;
        private readonly List<ServerProfile> _profiles;

        public ServerManageForm(ProfileStore store)
        {
            _store = store;
            _profiles = _store.Load();

            Text = "서버 관리";
            Width = 400;
            Height = 360;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _list = new ListBox { Left = 16, Top = 16, Width = 260, Height = 270, DisplayMember = "DisplayName" };
            RefreshList();

            var addBtn = new Button { Text = "추가", Left = 288, Top = 16, Width = 90, Height = 32 };
            var editBtn = new Button { Text = "수정", Left = 288, Top = 52, Width = 90, Height = 32 };
            var delBtn = new Button { Text = "삭제", Left = 288, Top = 88, Width = 90, Height = 32 };
            var closeBtn = new Button { Text = "닫기", Left = 288, Top = 254, Width = 90, Height = 32 };

            addBtn.Click += (s, e) =>
            {
                using var editForm = new ServerEditForm();
                if (editForm.ShowDialog(this) == DialogResult.OK)
                {
                    _profiles.Add(editForm.Result);
                    _store.Save(_profiles);
                    RefreshList();
                }
            };

            editBtn.Click += (s, e) =>
            {
                if (_list.SelectedItem is ServerProfile profile)
                {
                    using var editForm = new ServerEditForm(profile);
                    if (editForm.ShowDialog(this) == DialogResult.OK)
                    {
                        _store.Save(_profiles);
                        RefreshList();
                    }
                }
                else
                {
                    MessageBox.Show(this, "수정할 서버를 선택하세요.", "알림");
                }
            };

            delBtn.Click += (s, e) =>
            {
                if (_list.SelectedItem is ServerProfile profile)
                {
                    if (MessageBox.Show(this, $"'{profile.Name}' 서버를 삭제할까요?", "확인",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        _profiles.Remove(profile);
                        _store.Save(_profiles);
                        RefreshList();
                    }
                }
                else
                {
                    MessageBox.Show(this, "삭제할 서버를 선택하세요.", "알림");
                }
            };

            closeBtn.Click += (s, e) => Close();

            Controls.Add(_list);
            Controls.Add(addBtn);
            Controls.Add(editBtn);
            Controls.Add(delBtn);
            Controls.Add(closeBtn);
        }

        private void RefreshList()
        {
            _list.DataSource = null;
            _list.DataSource = _profiles;
            _list.DisplayMember = "DisplayName";
        }
    }
}

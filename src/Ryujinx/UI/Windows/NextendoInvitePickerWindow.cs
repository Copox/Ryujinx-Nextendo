using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.HLE.HOS.Applets.MyPage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo] The friend picker a game opens to invite friends (MyPage). Sends the invitation through
    /// the account server; <see cref="Sent"/> tells the game whether it went out.
    /// </summary>
    public class NextendoInvitePickerWindow : Window
    {
        private readonly FriendInvitationRequest _request;
        private readonly int _limit;
        private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 8) };
        private readonly StackPanel _list = new() { Spacing = 2 };
        private readonly Button _send = new() { IsEnabled = false };
        private readonly List<(TextBlock Header, List<(CheckBox Box, string Name)> Rows)> _sections = [];

        public bool Sent { get; private set; }

        public NextendoInvitePickerWindow(FriendInvitationRequest request)
        {
            _request = request;
            // The account server takes at most 15 recipients per invitation.
            _limit = (int)Math.Min(request.RecipientLimit, 15);

            Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_InvitePickerTitle];
            Width = 460;
            Height = 480;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _message.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_InvitePickerLoading];
            _search.Watermark = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_InvitePickerSearch];
            _search.TextChanged += (_, _) => ApplyFilter();
            _send.Content = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_InvitePickerSend];
            _send.Click += async (_, _) => await SendAsync();

            Button cancel = new() { Content = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CancelButton] };
            cancel.Click += (_, _) => Close();

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { _send, cancel },
            };

            DockPanel root = new() { Margin = new Thickness(16) };
            DockPanel.SetDock(_message, Dock.Top);
            DockPanel.SetDock(_search, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(_message);
            root.Children.Add(_search);
            root.Children.Add(buttons);
            root.Children.Add(new ScrollViewer { Content = _list });
            Content = root;

            Opened += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            (List<NextendoApi.Friend> friends, _) = await NextendoApi.GetSocialAsync();
            if (_request.Mode == 9)
            {
                friends = friends.Where(f => _request.AccountIds.Contains(f.Pid)).ToList();
            }

            AddSection(LocaleKeys.Dialog_Nextendo_InvitePickerOnlineFormat, friends.Where(f => f.IsOnline));
            AddSection(LocaleKeys.Dialog_Nextendo_InvitePickerOfflineFormat, friends.Where(f => !f.IsOnline));

            _message.Text = friends.Count > 0
                ? LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_InvitePickerChooseFormat, _limit)
                : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_InvitePickerNone];
            _send.IsEnabled = friends.Count > 0;
        }

        private void AddSection(LocaleKeys titleFormat, IEnumerable<NextendoApi.Friend> source)
        {
            List<NextendoApi.Friend> friends = source.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (friends.Count == 0)
            {
                return;
            }

            TextBlock header = new()
            {
                Text = LocaleManager.Instance.UpdateAndGetDynamicValue(titleFormat, friends.Count),
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, _sections.Count == 0 ? 0 : 10, 0, 4),
            };
            _list.Children.Add(header);

            List<(CheckBox, string)> rows = [];
            foreach (NextendoApi.Friend friend in friends)
            {
                string label = friend.Name;
                string game = friend.IsOnline ? NextendoGameNames.Resolve(friend.AppId) : null;
                if (!string.IsNullOrEmpty(game))
                {
                    label += "  ·  " + LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_FriendPlayingFormat, game);
                }

                CheckBox box = new()
                {
                    Content = label,
                    Tag = friend.Pid,
                    IsChecked = _request.Mode == 9,
                    Opacity = friend.IsOnline ? 1.0 : 0.55,
                };
                box.IsCheckedChanged += (_, _) => EnforceLimit(box);
                _list.Children.Add(box);
                rows.Add((box, friend.Name));
            }

            _sections.Add((header, rows));
        }

        private IEnumerable<CheckBox> Boxes => _sections.SelectMany(s => s.Rows).Select(r => r.Box);

        private void EnforceLimit(CheckBox changed)
        {
            if (changed.IsChecked == true && Boxes.Count(b => b.IsChecked == true) > _limit)
            {
                changed.IsChecked = false;
                _message.Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_InvitePickerLimitFormat, _limit);
            }
        }

        // Hides friends whose name doesn't match, and a section header left with none; ticks survive.
        private void ApplyFilter()
        {
            string text = _search.Text ?? "";
            foreach ((TextBlock header, List<(CheckBox Box, string Name)> rows) in _sections)
            {
                bool any = false;
                foreach ((CheckBox box, string name) in rows)
                {
                    box.IsVisible = text.Length == 0 || name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
                    any |= box.IsVisible;
                }

                header.IsVisible = any;
            }
        }

        private async Task SendAsync()
        {
            List<ulong> recipients = Boxes.Where(b => b.IsChecked == true).Select(b => (ulong)b.Tag).ToList();
            if (recipients.Count == 0)
            {
                _message.Text = LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.Dialog_Nextendo_InvitePickerChooseFormat, _limit);
                return;
            }

            _send.IsEnabled = false;
            _message.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_InvitePickerSending];

            (bool ok, string error) = await NextendoApi.SendGameInvitationAsync(_request.TitleId, recipients, _request.UserData, _request.Description);
            if (ok)
            {
                Sent = true;
                Close();
                return;
            }

            _message.Text = error;
            _send.IsEnabled = true;
        }
    }
}

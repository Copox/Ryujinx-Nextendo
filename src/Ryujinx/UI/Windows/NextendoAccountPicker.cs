using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccAccountManager = Ryujinx.HLE.HOS.Services.Account.Acc.AccountManager;
using AccUserProfile = Ryujinx.HLE.HOS.Services.Account.Acc.UserProfile;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo] « Qui joue ? » : le choix du compte Nextendo, quand plusieurs sont connectes.
    ///
    /// Repris du selecteur de profils de Harbor (MIT) : voile noir flou, titre, rangee de ronds,
    /// et les memes temps. Les tuiles entrent l'une apres l'autre (140 ms + 55 ms par tuile) ; au
    /// choix, la tuile choisie grossit (x1,14 en 440 ms) pendant que les autres retrecissent,
    /// s'estompent et se floutent, le titre remonte en s'effacant, le voile part a 340 ms et le
    /// choix est rendu a 680 ms.
    ///
    /// Il ne s'ouvre que s'il y a vraiment un choix : avec un seul compte Nextendo (meme a cote de
    /// profils locaux), ce compte est pris d'office.
    /// </summary>
    public sealed class NextendoAccountPicker : Window
    {
        /// <summary>The "add an account" tile was chosen.</summary>
        public const ulong AddAccount = 0;

        // cubic-bezier(0.16, 1, 0.3, 1), la courbe de Harbor
        private static readonly Easing _ease = new SplineEasing(0.16, 1, 0.3, 1);

        private readonly Border _veil;
        private readonly Grid _content;
        private readonly StackPanel _heading;
        private readonly List<(Control tile, ulong pid)> _tiles = [];
        private readonly TaskCompletionSource<ulong?> _result = new();
        private bool _leaving;

        /// <summary>
        /// Makes sure the right Nextendo account is active before something that uses it. Asks only
        /// when several accounts are connected. False if the player closed the picker.
        /// </summary>
        public static async Task<bool> EnsureChosen(Window owner = null)
        {
            if (NextendoAccount.Count <= 1)
            {
                // un seul compte : c'est lui, et c'est son profil qui joue
                if (NextendoAccount.IsLinked)
                {
                    OpenProfileOf(NextendoAccount.ProfileUserId);
                }

                return true;
            }

            ulong? pid = await Pick(owner, allowAdd: false);

            return pid is > 0;
        }

        /// <summary>Shows the picker. Returns the chosen account (already active), <see cref="AddAccount"/>
        /// for the add tile, or null when closed.</summary>
        public static async Task<ulong?> Pick(Window owner = null, bool allowAdd = false)
        {
            owner ??= RyujinxApp.MainWindow;

            // Une fenetre a part, OPAQUE, posee sur l'emulateur. Pas un calque dans la fenetre : le
            // rendu du menu HOME ou d'un jeu est une fenetre native qui passe au-dessus de tout
            // calque Avalonia. Pas transparente non plus : la transparence du systeme n'affichait que
            // son flou, sans le contenu. Sans fenetre visible (jeu lance en ligne de commande), le
            // compte actif est garde.
            if (owner is not { IsVisible: true })
            {
                return NextendoAccount.IsLinked ? NextendoAccount.Pid : null;
            }

            NextendoAccountPicker picker = new(allowAdd)
            {
                Position = owner.Position,
                Width = owner.Bounds.Width,
                Height = owner.Bounds.Height,
                WindowState = owner.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal,
            };

            picker.Show(owner);

            ulong? pid = await picker._result.Task;

            if (pid is > 0 && NextendoAccount.Activate(pid.Value))
            {
                OpenProfileOf(NextendoAccount.ProfileUserId);
            }

            return pid;
        }

        // The game then starts on the account's own local profile.
        private static void OpenProfileOf(string userId)
        {
            try
            {
                AccAccountManager am = RyujinxApp.MainWindow?.AccountManager;
                AccUserProfile profile = am?.GetAllUsers().FirstOrDefault(p => string.Equals(p.UserId.ToString(), userId, StringComparison.OrdinalIgnoreCase));

                if (profile != null && am.LastOpenedUser?.UserId != profile.UserId)
                {
                    am.OpenUser(profile.UserId);
                }
            }
            catch
            {
                // the account stays active even if its profile cannot be opened
            }
        }

        private NextendoAccountPicker(bool allowAdd)
        {
            SystemDecorations = SystemDecorations.None;
            ShowInTaskbar = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10));
            Title = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPickerTitle];

            _veil = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0, 0, 0)), // black/85
                Opacity = 0,
                Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(500), Easing = _ease }],
            };

            _heading = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPickerTitle],
                        FontSize = 40,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPickerSubtitle],
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
                Transitions =
                [
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = _ease },
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = _ease },
                ],
                RenderTransform = TransformOperations.Parse("translateY(0px)"),
            };

            WrapPanel row = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };

            List<NextendoAccount.Stored> accounts = [.. NextendoAccount.All];

            foreach (NextendoAccount.Stored account in accounts)
            {
                Control tile = MakeTile(account.Username, ProfileImage(account), account.Pid == NextendoAccount.Pid, add: false);
                ulong pid = account.Pid;
                tile.PointerReleased += (_, _) => Choose(tile, pid);
                _tiles.Add((tile, pid));
                row.Children.Add(tile);

                if (ProfileImage(account) == null)
                {
                    _ = LoadServerAvatar(tile, pid);
                }
            }

            if (allowAdd)
            {
                Control add = MakeTile(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPickerAdd], null, false, add: true);
                add.PointerReleased += (_, _) => Choose(add, AddAccount);
                _tiles.Add((add, AddAccount));
                row.Children.Add(add);
            }

            _content = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Opacity = 0,
                RenderTransform = TransformOperations.Parse("translateY(16px) scale(0.95)"),
                Transitions =
                [
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(500), Easing = _ease },
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(500), Easing = _ease },
                ],
            };
            _content.Children.Add(_heading);
            Grid.SetRow(row, 1);
            _content.Children.Add(row);

            Grid root = new() { Background = Brushes.Transparent };
            root.Children.Add(_veil);
            root.Children.Add(_content);

            // Un clic hors des tuiles, ou Echap, ferme sans choisir.
            root.PointerReleased += (_, e) =>
            {
                if (e.Source == root || e.Source == _veil)
                {
                    Leave(null);
                }
            };

            Content = root;

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Leave(null);
                }
            };

            // l'etat de depart est d'abord dessine, puis tout part vers l'etat final
            Opened += (_, _) => Dispatcher.UIThread.Post(Enter, DispatcherPriority.Background);
            Closed += (_, _) => _result.TrySetResult(null);
        }

        private static Bitmap ProfileImage(NextendoAccount.Stored account)
        {
            try
            {
                AccUserProfile profile = RyujinxApp.MainWindow?.AccountManager?.GetAllUsers()
                    .FirstOrDefault(p => string.Equals(p.UserId.ToString(), account.ProfileUserId, StringComparison.OrdinalIgnoreCase));

                if (profile?.Image is { Length: > 0 } image)
                {
                    return new Bitmap(new MemoryStream(image));
                }
            }
            catch
            {
                // no local picture
            }

            return null;
        }

        private static async Task LoadServerAvatar(Control tile, ulong pid)
        {
            try
            {
                byte[] avatar = await NextendoApi.GetAvatarAsync(pid, "/api/avatar");

                if (avatar is { Length: > 0 } && tile.GetLogicalDescendants().OfType<Image>().FirstOrDefault() is Image image)
                {
                    image.Source = new Bitmap(new MemoryStream(avatar));

                    if (image.Parent is Panel holder)
                    {
                        foreach (TextBlock initial in holder.Children.OfType<TextBlock>())
                        {
                            initial.IsVisible = false;
                        }
                    }
                }
            }
            catch
            {
                // the initial stays
            }
        }

        private static readonly IBrush _ringIdle = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush _ringHover = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));

        private Control MakeTile(string name, Bitmap picture, bool active, bool add)
        {
            Control face;

            if (add)
            {
                face = new Panel
                {
                    Width = 96,
                    Height = 96,
                    Children =
                    {
                        new Ellipse
                        {
                            Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                            StrokeThickness = 2,
                            StrokeDashArray = [4, 3],
                        },
                        new TextBlock
                        {
                            Text = "+",
                            FontSize = 40,
                            FontWeight = FontWeight.Light,
                            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, -6, 0, 0),
                        },
                    },
                };
            }
            else
            {
                Border photo = new()
                {
                    Width = 96,
                    Height = 96,
                    CornerRadius = new CornerRadius(48),
                    ClipToBounds = true,
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)),
                    Child = new Image { Source = picture, Stretch = Stretch.UniformToFill },
                };

                if (picture == null)
                {
                    photo.Child = new Panel
                    {
                        Children =
                        {
                            new Image { Stretch = Stretch.UniformToFill },
                            new TextBlock
                            {
                                Text = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant(),
                                FontSize = 36,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = Brushes.White,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                    };
                }

                Border ring = new()
                {
                    Width = 104,
                    Height = 104,
                    CornerRadius = new CornerRadius(52),
                    BorderThickness = new Thickness(2),
                    BorderBrush = _ringIdle,
                };

                face = new Panel { Width = 104, Height = 104, Children = { photo, ring } };
            }

            StackPanel tile = new()
            {
                Spacing = 10,
                Width = 136,
                Margin = new Thickness(12, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Opacity = 0,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = TransformOperations.Parse("translateY(12px)"),
                Transitions =
                [
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = _ease },
                    new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = _ease },
                    new EffectTransition { Property = EffectProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = _ease },
                ],
                Children = { face },
            };
            face.HorizontalAlignment = HorizontalAlignment.Center;

            tile.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 136,
            });

            if (active)
            {
                tile.Children.Add(new TextBlock
                {
                    Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_AccountPickerActive].ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 1.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }

            // Survol : l'anneau s'allume et la tuile grossit un peu (x1,04 comme Harbor).
            tile.PointerEntered += (_, _) =>
            {
                if (_leaving || tile.Opacity < 1)
                {
                    return;
                }

                tile.RenderTransform = TransformOperations.Parse("scale(1.04)");

                if (face is Panel { Children: [_, Border r] })
                {
                    r.BorderBrush = _ringHover;
                }
            };
            tile.PointerExited += (_, _) =>
            {
                if (_leaving)
                {
                    return;
                }

                tile.RenderTransform = TransformOperations.Parse("scale(1)");

                if (face is Panel { Children: [_, Border r] })
                {
                    r.BorderBrush = _ringIdle;
                }
            };

            return tile;
        }

        private void Enter()
        {
            Activate();
            Focus();

            _veil.Opacity = 1;
            _content.Opacity = 1;
            _content.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");

            for (int i = 0; i < _tiles.Count; i++)
            {
                Control tile = _tiles[i].tile;

                DispatcherTimer.RunOnce(() =>
                {
                    if (!_leaving)
                    {
                        tile.Opacity = 1;
                        tile.RenderTransform = TransformOperations.Parse("translateY(0px)");
                    }
                }, TimeSpan.FromMilliseconds(140 + Math.Min(i, 8) * 55));
            }
        }

        private void Choose(Control chosen, ulong pid)
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;

            foreach ((Control tile, _) in _tiles)
            {
                if (tile == chosen)
                {
                    tile.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(440), Easing = _ease }];
                    tile.RenderTransform = TransformOperations.Parse("scale(1.14)");
                }
                else
                {
                    tile.Opacity = 0;
                    tile.RenderTransform = TransformOperations.Parse("scale(0.9)");
                    tile.Effect = new BlurEffect { Radius = 2 };
                }
            }

            _heading.Opacity = 0;
            _heading.RenderTransform = TransformOperations.Parse("translateY(-4px)");

            DispatcherTimer.RunOnce(() =>
            {
                _veil.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = _ease }];
                _content.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = _ease }];
                _veil.Opacity = 0;
                _content.Opacity = 0;
            }, TimeSpan.FromMilliseconds(340));

            DispatcherTimer.RunOnce(() =>
            {
                _result.TrySetResult(pid);
                Close();
            }, TimeSpan.FromMilliseconds(680));
        }

        private void Leave(ulong? pid)
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;

            _veil.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = _ease }];
            _content.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = _ease }];
            _veil.Opacity = 0;
            _content.Opacity = 0;

            DispatcherTimer.RunOnce(() =>
            {
                _result.TrySetResult(pid);
                Close();
            }, TimeSpan.FromMilliseconds(340));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RawInput.Touchpad
{
    public partial class MainWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public bool TouchpadExists
        {
            get { return (bool)GetValue(TouchpadExistsProperty); }
            set { SetValue(TouchpadExistsProperty, value); }
        }

        public static readonly DependencyProperty TouchpadExistsProperty =
            DependencyProperty.Register(
                "TouchpadExists",
                typeof(bool),
                typeof(MainWindow),
                new PropertyMetadata(false)
            );

        public string TouchpadContacts
        {
            get { return (string)GetValue(TouchpadContactsProperty); }
            set { SetValue(TouchpadContactsProperty, value); }
        }

        public static readonly DependencyProperty TouchpadContactsProperty =
            DependencyProperty.Register(
                "TouchpadContacts",
                typeof(string),
                typeof(MainWindow),
                new PropertyMetadata(null)
            );

        // reference to tray icon
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        // tracks if touch is showing, used to avoid updating the icon if it is already correct
        private int _touchesShowing = 0;

        // when was the last touch event received
        private long _lastTouchTime = 0;

        // timer to expire last touch event to detect when no one is touching the track pad anymore
        private Timer _touchTimer;

        // list of all received touch events
        private readonly List<string> _log = new();

        public MainWindow()
        {
            InitializeComponent();

            // add tray icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Touchpad Monitor",
                Visible = true,
            };
            SetIconState(System.Drawing.Color.Red, ' ');
            // restore window on tray icon click
            _notifyIcon.Click += (s, e) => RestoreWindow();
            // keep window on top when showing
            Topmost = true;
            // start window in bottom right corner
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Width - Width;
            Top = workingArea.Height - Height;
            // start timer to track expired touches
            _touchTimer = new Timer(TouchTimerCallback, null, 0, 50);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _notifyIcon.Dispose();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _notifyIcon.Visible = true;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var targetSource = PresentationSource.FromVisual(this) as HwndSource;
            targetSource?.AddHook(WndProc);

            TouchpadExists = TouchpadHelper.Exists();

            _log.Add($"Precision touchpad exists: {TouchpadExists}");

            if (TouchpadExists)
            {
                var success = TouchpadHelper.RegisterInput(targetSource.Handle);

                _log.Add($"Precision touchpad registered: {success}");
            }

            Visibility = Visibility.Hidden;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case TouchpadHelper.WM_INPUT:
                    var contacts = TouchpadHelper.ParseInput(lParam);
                    _lastTouchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    TouchpadContacts = string.Join(
                        Environment.NewLine,
                        contacts.Select(x => x.ToString())
                    );

                    var touchCount = contacts.Length;

                    if (touchCount != _touchesShowing)
                    {
                        _touchesShowing = touchCount;
                        Background = new SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(144, 238, 144)
                        );
                        SetIconState(System.Drawing.Color.Green, touchCount.ToString().ToCharArray()[0]);
                    }

                    _log.Add("---");
                    _log.Add(TouchpadContacts);
                    break;
            }
            return IntPtr.Zero;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _log));
        }

        private void TouchTimerCallback(object state)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now - _lastTouchTime > 100 && _touchesShowing > 0)
            {
                Dispatcher.Invoke(() =>
                {
                    _touchesShowing = 0;
                    TouchpadContacts = "";
                    Background = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(238, 144, 144)
                    );
                    SetIconState(System.Drawing.Color.Red, ' ');
                });
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _notifyIcon.Visible = false;
        }

        private void SetIconState(System.Drawing.Color color, char label)
        {
            using (var icon = CreateCircleIcon(color, label))
            {
                _notifyIcon.Icon = icon;
                DestroyIcon(icon.Handle);
            }
        }

        private static Icon CreateCircleIcon(System.Drawing.Color color, char character)
        {
            int size = 32;
            using (var bitmap = new Bitmap(size, size))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    
                    // Draw the circle with the specified color
                    using (var brush = new SolidBrush(color))
                    {
                        graphics.FillEllipse(brush, 0, 0, size, size);
                    }

                    // Set up the font and brush for the character
                    using var font = new Font(System.Drawing.FontFamily.GenericSansSerif, 6, System.Drawing.FontStyle.Bold);
                    using var textBrush = new SolidBrush(System.Drawing.Color.White);
                    // Measure the size of the character to center it
                    SizeF textSize = graphics.MeasureString(character.ToString(), font);

                    // Calculate the position to center the character
                    float x = (size - textSize.Width) / 2;
                    float y = (size - textSize.Height) / 2;

                    // Draw the character centered on the circle
                    graphics.DrawString(character.ToString(), font, textBrush, x, y);
                }

                IntPtr hIcon = bitmap.GetHicon();
                return System.Drawing.Icon.FromHandle(hIcon);
            }
        }

    }
}

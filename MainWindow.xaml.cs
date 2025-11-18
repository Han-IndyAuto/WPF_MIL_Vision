using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IndyVision
{
    public partial class MainWindow : Window
    {
        private Point _origin;
        private Point _start;
        private bool _isDragging = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;
            vm?.Cleanup();
        }

        // 1. 이미지가 로드되면 화면 맞춤 실행
        private void ImgView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => FitImageToScreen(), DispatcherPriority.ContextIdle);
        }

        // 2. 창 크기가 변하면 화면 맞춤 실행
        private void ZoomBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ImgView.Source != null) FitImageToScreen();
        }

        // 3. 프로그램 시작 시 화면 맞춤 실행
        private void ZoomBorder_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => FitImageToScreen(), DispatcherPriority.ContextIdle);
        }

        // ---------------------------------------------------------
        // [화면 맞춤 함수] (디버깅 코드 제거됨)
        // ---------------------------------------------------------
        public void FitImageToScreen()
        {
            // 화면 갱신
            ZoomBorder.UpdateLayout();
            ImgView.UpdateLayout();

            if (ImgView.Source == null || ZoomBorder.ActualWidth == 0 || ZoomBorder.ActualHeight == 0)
                return;

            var imageSource = ImgView.Source as BitmapSource;
            // [중요] DPI 호환성을 위해 Width 사용
            if (imageSource == null || imageSource.Width == 0 || imageSource.Height == 0) return;

            // 초기화
            imgScale.ScaleX = 1.0;
            imgScale.ScaleY = 1.0;
            imgTranslate.X = 0;
            imgTranslate.Y = 0;

            // 배율 계산
            double scaleX = ZoomBorder.ActualWidth / imageSource.Width;
            double scaleY = ZoomBorder.ActualHeight / imageSource.Height;
            double scale = Math.Min(scaleX, scaleY);

            if (scale > 1.0) scale = 1.0; // 확대 금지

            // 적용 (95% 크기)
            imgScale.ScaleX = scale * 0.95;
            imgScale.ScaleY = scale * 0.95;

            // 중앙 좌표 계산
            double finalWidth = imageSource.Width * imgScale.ScaleX;
            double finalHeight = imageSource.Height * imgScale.ScaleY;

            imgTranslate.X = (ZoomBorder.ActualWidth - finalWidth) / 2;
            imgTranslate.Y = (ZoomBorder.ActualHeight - finalHeight) / 2;
        }

        // ---------------------------------------------------------
        // [마우스 조작] 줌 & 팬
        // ---------------------------------------------------------
        private void ZoomBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImgView.Source == null) return;

            Point p = e.GetPosition(ZoomBorder);
            double zoom = e.Delta > 0 ? 1.2 : (1.0 / 1.2);

            imgScale.ScaleX *= zoom;
            imgScale.ScaleY *= zoom;

            imgTranslate.X = p.X - (p.X - imgTranslate.X) * zoom;
            imgTranslate.Y = p.Y - (p.Y - imgTranslate.Y) * zoom;
        }

        private void ZoomBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImgView.Source == null) return;
            var border = sender as Border;
            border.CaptureMouse();
            _start = e.GetPosition(border);
            _origin = new Point(imgTranslate.X, imgTranslate.Y);
            _isDragging = true;
            Cursor = Cursors.Hand;
        }

        private void ZoomBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                var border = sender as Border;
                border.ReleaseMouseCapture();
                _isDragging = false;
                Cursor = Cursors.Arrow;
            }
        }

        private void ZoomBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var border = sender as Border;
                Point v = e.GetPosition(border);
                imgTranslate.X = _origin.X + (v.X - _start.X);
                imgTranslate.Y = _origin.Y + (v.Y - _start.Y);
            }
        }

        private void ZoomBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            FitImageToScreen();
        }
    }
}
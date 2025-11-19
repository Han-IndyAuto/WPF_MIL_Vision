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

        // ROI 관련 변수
        private bool _isRoiDrawing = false;
        private Point _roiStartPoint;   // 이미지 기준 좌표
        private Rect _currentRoiRect;   // 계산된 ROI 영역 (X, Y, W, H)

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

            /*
            // [디버깅] 계산된 결과를 윈도우 제목에 표시 (성공 여부 확인용)
            this.Title = $"결과: W={imageSource.Width}, H={imageSource.Height}, " +
                         $"Border={ZoomBorder.ActualWidth:F0}x{ZoomBorder.ActualHeight:F0}, " +
                         $"Scale={scale:F4}, TransX={imgTranslate.X:F0}, TransY={imgTranslate.Y:F0}";
            */
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

        private void ZoomBorder_MouseMove(object sender, MouseEventArgs e)
        {

            // ROI 그리기 중일때
            if(_isRoiDrawing)
            {
                Point currentPos = e.GetPosition(ImgView);
                var bitmap = ImgView.Source as BitmapSource;

                // 이미지 범위 제한 (Clamp)
                if (currentPos.X < 0) currentPos.X = 0;
                if (currentPos.Y < 0) currentPos.Y = 0;
                if (currentPos.X > bitmap.PixelWidth) currentPos.X = bitmap.PixelWidth;
                if (currentPos.Y > bitmap.PixelHeight) currentPos.Y = bitmap.PixelHeight;

                // 시각적 사각형 업데이트
                UpdateRoiVisual(_roiStartPoint, currentPos);

            }
            else if (_isDragging)
            {
                var border = sender as Border;
                Point v = e.GetPosition(border);
                imgTranslate.X = _origin.X + (v.X - _start.X);
                imgTranslate.Y = _origin.Y + (v.Y - _start.Y);
            }

            // 마우스 좌표 표시 로직
            // MVVM 패턴에서 View(화면)가 ViewModel(데이터/로직)에 접근하기 위한 전형적인 코드.
            // this: 현재 코드(MainWindow.xaml.cs)가 속한 MainWindow 창 자체를 의미.
            // .DataContext: WPF의 핵심 속성으로 이 창(View)이 어떤 데이터 덩어리를 바라보고 있는지 저장하는 변수.
            //  MainWindow.xaml에서 <local:MainViewModel/> 로 설정해 두었기 때문에, 여기에는 MainViewModel 객체가 들어있음.
            //  하지만, 컴퓨터는 이변수를 구체적인 MainViewModel 이 아니라, 그냥 범용 객체(object)로 알고있으며,
            //  as MainViewModel: 형변환으로, 범용 객체인줄 알았는데, 알고보니 MainViewModel 이지? 그렇다면 MainViewModel 로 바꿔달라고 명령하는 것입니다.
            //  이렇게 해야 MainViewModel 안에 있는 속성들 (MouseCoordinationInfo, DisplayImage 등)을 코드에서 사용할수 있습니다.
            // 마지막으로 변환된 객체를 vm 이라는 변수에 담아둡니다.
            // 이코드가 없으면, MainViewModel 안에 있는 MouseCoordinationInfo (좌표문자열) 속성에 접근할 수 없습니다.
            // 마우스가 움직임(MouseMove) -> 좌표 계산 (View에서 함) -> "이 좌표 값을 화면에 띄워줘!" 하고 ViewModel에게 전달해야 함.
            var vm = this.DataContext as MainViewModel;
            if (vm != null)
            {
                // 이미지가 로드되어 있는지 확인.
                if(ImgView.Source is BitmapSource bitmap)
                {
                    // 이미지 컨트롤(ImgView) 기준의 좌표를 가져옴.
                    // RenderTransform(줌/이동)과 상관 없이 원본 이미지 기준의 픽셀 좌표를 구함.
                    Point p = e.GetPosition(ImgView);

                    int currentX = (int)p.X;
                    int currentY = (int)p.Y;

                    // 마우스가 이미지 영역 안에 있을때만 표시.
                    if(currentX >= 0 && currentX < bitmap.PixelWidth && 
                        currentY >= 0 && currentY < bitmap.PixelHeight)
                    {
                        vm.MouseCoordinationInfo = $"(X: {currentX}, Y: {currentY})";
                    }
                    else
                    {
                        // 이미지 영역 밖
                        vm.MouseCoordinationInfo = "(X: 0, Y: 0)";
                    }
                }
            }
        }

        private void ZoomBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            FitImageToScreen();
        }

        private void ZoomBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;

            // ROI 모드이고, 왼쪽 버튼 클릭 시 -> 그리기 시작
            if(vm != null && vm.SelectedAlgorithm != null && 
                vm.SelectedAlgorithm.Contains("ROI") && 
                e.ChangedButton == MouseButton.Left &&
                ImgView.Source != null)
            {
                // 이미지 기준 좌표 계산
                Point mousePos = e.GetPosition(ImgView);
                var bitmap = ImgView.Source as BitmapSource;

                // 이미지 내부인지 확인
                if(mousePos.X >= 0 && mousePos.X < bitmap.PixelWidth && mousePos.Y >= 0 && mousePos.Y < bitmap.PixelHeight)
                {
                    _isRoiDrawing = true;
                    _roiStartPoint = mousePos;

                    // 사각형 초기화 및 표시
                    RoiRect.Visibility = Visibility.Visible;
                    RoiRect.Width = 0;
                    RoiRect.Height = 0;

                    // 캔버스상 위치 설정을 위해 랜더링 변환 적용. (줌/이동 고려)
                    UpdateRoiVisual(mousePos, mousePos);

                    // 마우스 캡쳐 (밖으로 나가도 이벤트 받기 위해)
                    ImgCanvas.CaptureMouse();
                }
            }


            // 가운데 버튼(휠 클릭)인지 확인
            else if (e.ChangedButton == MouseButton.Middle && ImgView.Source != null)
            {
                var border = sender as Border;
                border.CaptureMouse();
                _start = e.GetPosition(border);
                _origin = new Point(imgTranslate.X, imgTranslate.Y);
                _isDragging = true;

                // 커서를 이동 모양(십자 화살표)으로 변경하여 드래그 중임을 표시
                Cursor = Cursors.SizeAll;
            }

            // (옵션) 우클릭 시 화면 맞춤 기능 유지
            //if (e.ChangedButton == MouseButton.Right)
            //{
            //    FitImageToScreen();
            //}
        }

        private void ZoomBorder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // ROI 그리기 종료
            if (_isRoiDrawing)
            {
                _isRoiDrawing = false;
                ImgCanvas.ReleaseMouseCapture();

                // 최종 ROI 좌표 저장 (나중에 자르기/저장 할 때 씀)
                // 정규화 (음수 크기 방지)
                double x = Math.Min(_roiStartPoint.X, RoiRect.Tag is Point p ? p.X : 0); // Tag에 끝점 저장했다고 가정하거나 다시 계산
                                                                                         // -> 단순화를 위해 UpdateRoiVisual에서 _currentRoiRect를 갱신하도록 함
            }

            // [변경] 드래그 중이었고, 뗀 버튼이 가운데 버튼이라면 종료
            if (_isDragging && e.ChangedButton == MouseButton.Middle)
            {
                var border = sender as Border;
                border.ReleaseMouseCapture();
                _isDragging = false;
                Cursor = Cursors.Arrow;
            }
        }

        // [수정됨] 좌표 변환 로직 수정
        private void UpdateRoiVisual(Point start, Point end)
        {
            double x = Math.Min(start.X, end.X);
            double y = Math.Min(start.Y, end.Y);
            double w = Math.Abs(end.X - start.X);
            double h = Math.Abs(end.Y - start.Y);

            // 1. 논리적 ROI 데이터 저장 (이미지 기준 픽셀)
            _currentRoiRect = new Rect(x, y, w, h);

            // 2. 화면 표시용 좌표 변환 (Zoom/Pan 적용)
            // [작성하신 계산식은 정확합니다]
            double screenX = x * imgScale.ScaleX + imgTranslate.X;
            double screenY = y * imgScale.ScaleY + imgTranslate.Y;
            double screenW = w * imgScale.ScaleX;
            double screenH = h * imgScale.ScaleY;

            // [핵심 수정 사항]
            // 문제 원인: RoiRect.RenderTransform = ImgView.RenderTransform; 
            // 이 코드가 있으면 '화면 좌표'가 아니라 '이미지 좌표'를 넣어야 하는데, 
            // Canvas.Left 위치 계산과 Scale 적용 방식이 충돌하여 위치가 어긋납니다.

            // 해결책: RenderTransform을 공유하지 말고, 
            // 위에서 계산한 'screenX/Y/W/H' (최종 화면 좌표)를 직접 대입하는 것이 가장 정확하고 깔끔합니다.

            RoiRect.RenderTransform = null; // 중요: 기존 이미지의 Transform을 따라가지 않도록 해제

            RoiRect.Width = screenW;        // 계산된 화면 크기 적용
            RoiRect.Height = screenH;
            Canvas.SetLeft(RoiRect, screenX); // 계산된 화면 위치 적용
            Canvas.SetTop(RoiRect, screenY);
        }
       
        private void MenuItem_Crop_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoiRect.Width <= 0 || _currentRoiRect.Height <= 0) return;

            var vm = this.DataContext as MainViewModel;
            if (vm != null)
            {
                // 정수형으로 변환하여 전달
                vm.CropImage((int)_currentRoiRect.X, (int)_currentRoiRect.Y, (int)_currentRoiRect.Width, (int)_currentRoiRect.Height);

                // 잘라낸 후 사각형 숨기기
                RoiRect.Visibility = Visibility.Collapsed;
                // 뷰 리셋 (선택 사항)
                FitImageToScreen();
            }
        }

        private void MenuItem_Save_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoiRect.Width <= 0 || _currentRoiRect.Height <= 0) return;

            var vm = this.DataContext as MainViewModel;
            if (vm != null)
            {
                Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Filter = "Bitmap (*.bmp)|*.bmp|JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png";
                dlg.FileName = "ROI_Image";

                if (dlg.ShowDialog() == true)
                {
                    vm.SaveRoiImage(dlg.FileName, (int)_currentRoiRect.X, (int)_currentRoiRect.Y, (int)_currentRoiRect.Width, (int)_currentRoiRect.Height);

                    // [추가] 저장이 완료되면 사각형을 숨깁니다.
                    RoiRect.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}
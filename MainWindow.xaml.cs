using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IndyVision
{
    /// <summary>
    /// 이미지뷰어의 모든 동작(화면 맞춤, 줌, 이동, ROI 그리기, 좌표 표시)을 제어.
    /// </summary>
    public partial class MainWindow : Window
    {
        // 이미지 이동 [Pan]을 위한 변수들.
        private Point _origin;              // 이동 시작 시점의 이미지 위치 (X, Y)
        private Point _start;               // 이동 시작 시점의 마우스 포인터 위칭 (X, Y)
        private bool _isDragging = false;   // 현재 마우스로 끌고 있는지 여부 (true/false)

        // ROI 관련 변수
        private bool _isRoiDrawing = false; // 현재 ROI를 그리고 있는지 여부.
        private Point _roiStartPoint;       // 이미지 기준 좌표: ROI 사각형을 그리기 시작한 점 (클릭한 곳)
        private Rect _currentRoiRect;       // 최종적으로 계산된 ROI 영역 (X, Y, W, H)

        public MainWindow()
        {
            InitializeComponent();          // XAML에 그려진 UI 요소들을 메모리에 로드하고 화면에 띄웁니다.
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 창이 닫힐 때(Closing), ViewModel에게 "청소할 거 있으면 해라"고 신호를 줍니다.
            var vm = this.DataContext as MainViewModel;
            // MIL 라이브러리 메모리 해제 등을 수행
            vm?.Cleanup();
        }

        // 1. 이미지가 로드되면 화면 맞춤 실행
        private void ImgView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // "화면 그리는 일이 다 끝나고 한가해지면(ContextIdle), FitImageToScreen을 실행해라"
            // 즉시 실행하면 UI가 꼬일 수 있어서 안전하게 예약 실행.
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
        // 이미지를 화면 중앙에 예쁘게 맞추는 수학 계산
        // ---------------------------------------------------------
        public void FitImageToScreen()
        {
            // 화면 강제 갱신 (현재 크기를 정확히 알기 위해)
            ZoomBorder.UpdateLayout();
            ImgView.UpdateLayout();

            if (ImgView.Source == null || ZoomBorder.ActualWidth == 0 || ZoomBorder.ActualHeight == 0)
                return;

            var imageSource = ImgView.Source as BitmapSource;
            // [중요] DPI 호환성을 위해 Width 사용
            if (imageSource == null || imageSource.Width == 0 || imageSource.Height == 0) return;

            // [초기화] 확대/이동 상태를 리셋합니다.
            imgScale.ScaleX = 1.0;      // 배율 1배
            imgScale.ScaleY = 1.0;       
            imgTranslate.X = 0;         // 위치 0,0
            imgTranslate.Y = 0;

            // [배율 계산] 화면 너비/이미지 너비 비율을 계산합니다.
            double scaleX = ZoomBorder.ActualWidth / imageSource.Width;
            double scaleY = ZoomBorder.ActualHeight / imageSource.Height;

            // 가로/세로 중 더 작은 비율을 선택해야 이미지가 잘리지 않고 다 들어옵니다.
            double scale = Math.Min(scaleX, scaleY);

            // 이미지가 화면보다 작아도 억지로 늘리지 않음 (1배 유지)
            if (scale > 1.0) scale = 1.0; // 확대 금지

            // [적용] 계산된 배율을 적용하되, 꽉 차지 않게 95%만 씁니다. (여백 미)
            imgScale.ScaleX = scale * 0.95;
            imgScale.ScaleY = scale * 0.95;

            // [중앙 정렬] 화면 중앙에 오도록 위치 계산
            // (화면너비 - 줄어든이미지너비) / 2 = 왼쪽 여백
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

            // 현재 마우스 위치를 기준으로 확대/축소하기 위해 좌표를 구합니다.
            Point p = e.GetPosition(ZoomBorder);

            // 휠을 올리면(Delta > 0) 1.2배 확대, 내리면 1.2배 축소
            double zoom = e.Delta > 0 ? 1.2 : (1.0 / 1.2);

            // 배율 적용
            imgScale.ScaleX *= zoom;
            imgScale.ScaleY *= zoom;

            // [위치 보정] 마우스 포인터 위치(p)가 줌 후에도 같은 곳에 있도록 이동시킵니다.
            // (이 수식은 줌 기능의 표준 공식입니다)
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
                // 시작점(_roiStartPoint)과 현재점(currentPos)으로 사각형을 업데이트
                UpdateRoiVisual(_roiStartPoint, currentPos);

            }
            else if (_isDragging)       // 이미지 이동 중일 때
            {
                var border = sender as Border;
                Point v = e.GetPosition(border);    // 현재 마우스 위치

                // 이동 거리 = 현재위치(v) - 시작위치(_start)
                // 새 이미지 위치 = 원래위치(_origin) + 이동거리
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
                Point mousePos = e.GetPosition(ImgView);    // 클릭한 위치(이미지 기준)를 가져옵니다.
                var bitmap = ImgView.Source as BitmapSource;

                // 이미지 내부인지 확인
                if(mousePos.X >= 0 && mousePos.X < bitmap.PixelWidth && mousePos.Y >= 0 && mousePos.Y < bitmap.PixelHeight)
                {
                    _isRoiDrawing = true;       // 지금부터 그린다 표시.
                    _roiStartPoint = mousePos;  // 시작점 저장.

                    // 사각형 초기화 및 표시
                    RoiRect.Visibility = Visibility.Visible;    // 빨깐 사각형을 보이게 함.
                    // 처음에는 크기가 0
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
                border.CaptureMouse();          // 마우스 납치

                _start = e.GetPosition(border); // 드래그 시작 위치 저장.
                _origin = new Point(imgTranslate.X, imgTranslate.Y);    // 현재 이미지 위치 저장
                _isDragging = true;             // "이동 중!" 표시

                // 커서를 이동 모양(십자 화살표)으로 변경하여 드래그 중임을 표시
                Cursor = Cursors.SizeAll;
            }

            // (옵션) 우클릭 시 화면 맞춤 기능 유지
            //if (e.ChangedButton == MouseButton.Right && _isRoiDrawing == false) 
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
                ImgCanvas.ReleaseMouseCapture();   // 납치했던 마우스 놓아줌 

                // 최종 ROI 좌표 저장 (나중에 자르기/저장 할 때 씀)
                // 정규화 (음수 크기 방지)
                double x = Math.Min(_roiStartPoint.X, RoiRect.Tag is Point p ? p.X : 0); // Tag에 끝점 저장했다고 가정하거나 다시 계산
                                                                                         // -> 단순화를 위해 UpdateRoiVisual에서 _currentRoiRect를 갱신하도록 함
            }

            // [변경] 드래그 중이었고, 뗀 버튼이 가운데 버튼이라면 이동 종료
            if (_isDragging && e.ChangedButton == MouseButton.Middle)
            {
                var border = sender as Border;
                border.ReleaseMouseCapture();   // 마우스 놓아줌
                _isDragging = false;
                Cursor = Cursors.Arrow;         // 커서 모양 복구.
            }
        }

        // [수정됨] 좌표 변환 로직 수정
        // 시작점과 끝점 두 개를 받아서 "화면에 어떻게 그려야 할지" 계산하는 함수
        private void UpdateRoiVisual(Point start, Point end)
        {
            // 두 점 중 작은 값이 왼쪽/위쪽(X, Y), 차이값이 너비/높이(W, H)
            double x = Math.Min(start.X, end.X);
            double y = Math.Min(start.Y, end.Y);
            double w = Math.Abs(end.X - start.X);
            double h = Math.Abs(end.Y - start.Y);

            // 1. 논리적 ROI 데이터 저장 (이미지 기준 픽셀 - 나중에 자를 때 씀)
            _currentRoiRect = new Rect(x, y, w, h);

            // 2. 화면 표시용 좌표 변환 (Zoom/Pan 적용)
            // 이미지가 확대되어 있으면 사각형도 확대된 위치에 그려야 함
            // 공식: 이미지좌표 * 배율 + 이동거리
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

            // [중요] 빨간 사각형(Rectangle) 속성 변경
            RoiRect.RenderTransform = null; // 중요: 기존 이미지의 Transform을 따라가지 않도록 해제 : 충돌 방지용 초기화

            RoiRect.Width = screenW;        // 계산된 화면 크기 적용
            RoiRect.Height = screenH;
            Canvas.SetLeft(RoiRect, screenX);   // 캔버스 위에서의 X 위치
            Canvas.SetTop(RoiRect, screenY);    // 캔버스 위에서의 Y 위치
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
                FitImageToScreen();     // 잘린 이미지에 맞춰 화면 갱신
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
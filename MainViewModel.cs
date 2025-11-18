using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace IndyVision
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // _milService: 실제 이미지 처리를 담당하는 전문가(작업자)입니다.
        // ViewModel이 직접 이미지를 깎지 않고, 이 전문가에게 시킵니다.
        private MilService _milService;

        // 생성자: 이 클래스가 처음 만들어질 때(프로그램 켜질 때) 딱 한 번 실행됩니다.
        // 실행 시점은 MainWindow.xaml에서 DataContext로 이 ViewModel을 지정할 때입니다.
        public MainViewModel()
        {
            /*
             * 핵심: MainViewModel이 태어나자마자(생성자), 이미지 처리 담당자(MilService)를 준비시키고 메뉴판(AlgorithmList)을 작성합니다.
             */

            // 1. 작업자(MilService)를 고용(생성)합니다.
            _milService = new MilService();

            // 2. 콤보박스에 들어갈 메뉴판(알고리즘 목록)을 만듭니다.
            AlgorithmList = new ObservableCollection<string>
            {
                "Gray 처리",
                "Threshold (이진화)",
                "Adaptive Threshold (적응형 이진화)",
                "Morphology (모폴로지)",
                "Edge Detection (엣지 검출)"
            };
        }

        // --- Properties ---
        // 화면에 보여지는 데이터 (Properties)
        // 여기가 MVVM 패턴의 꽃입니다. 데이터가 변하면 화면이 자동으로 바뀝니다.

        // 화면에 표시되는 최종 이미지
        // _displayImage: 화면에 실제로 보여줄 이미지 데이터가 담긴 변수(금고)입니다.
        private ImageSource _displayImage;
        public ImageSource DisplayImage
        {
            // 화면이 "이미지 줘!" 하면 금고에서 꺼내줍니다.
            get => _displayImage;
            set 
            { 
                _displayImage = value;
                // "이미지가 바뀌었습니다!"라고 방송해서 화면이 다시 그려지게 합니다.
                OnPropertyChanged(); 
            }
        }

        // _showOriginal: "원본 보기" 체크박스 상태 (True/False)
        // 원본 보기 체크박스 바인딩
        private bool _showOriginal;
        public bool ShowOriginal
        {
            get => _showOriginal;
            set
            {
                _showOriginal = value;
                // 체크박스 상태가 바뀜을 알림
                OnPropertyChanged();

                // 체크 상태가 바뀌면 이미지를 다시 불러옴 (원본 vs 결과)
                // [중요] 체크박스를 껐다 켰다 할 때마다 즉시 이미지를 바꿔 끼워줍니다.
                // (체크됨: 원본 보여줘 / 체크해제: 결과 보여줘)
                UpdateDisplay();
            }
        }

        // 콤보박스에 연결될 리스트
        // ObservableCollection: UI와 대화하는 똑똑한 리스트
        // 일반 List<string>: 데이터를 담을 수는 있지만, 데이터가 추가되거나 삭제되어도 화면(ListBox, ComboBox)은 그 사실을 모릅니다. 그래서 화면이 갱신되지 않습니다.
        // ObservableCollection<string>: 데이터가 추가(Add)되거나 삭제(Remove)되면, **"나 내용물 바뀌었어! 화면 다시 그려!"**라고 UI에게 즉시 알림을 보냅니다.
        // AlgorithmList: 사용자가 화면에서 보게 될 **"알고리즘 메뉴 목록"**을 담고 있는 그릇
        public ObservableCollection<string> AlgorithmList { get; set; }

        //알고리즘 선택과 동적 파라미터(가장 중요한 로직)

        private string _selectedAlgorithm;
        public string SelectedAlgorithm
        {
            get => _selectedAlgorithm;
            set
            {
                _selectedAlgorithm = value;
                OnPropertyChanged();

                // [핵심 로직] 
                // 사용자가 "이진화"를 선택하면 -> 이진화용 슬라이더 설정(Params)을 만듭니다.
                // 사용자가 "모폴로지"를 선택하면 -> 모폴로지용 설정(Params)을 만듭니다.
                // 알고리즘 선택 시 해당 파라미터 객체 생성
                CreateParametersForAlgorithm(value);
            }
        }

        // 변수 선언: 부모 타입(AlgorithmParamsBase)으로 선언
        // 현재 선택된 알고리즘의 설정값 객체 (UI의 ContentControl과 바인딩)
        // _currentParameters: 현재 선택된 알고리즘의 설정값(객체)입니다.
        // 이 변수에 무엇이 들어가느냐에 따라 화면 오른쪽 아래 UI(슬라이더/입력창)가 바뀝니다.
        private AlgorithmParamsBase _currentParameters;
        public AlgorithmParamsBase CurrentParameters
        {
            get => _currentParameters;
            set { _currentParameters = value; OnPropertyChanged(); }
        }

        // --- Methods ---

        // 선택된 이름(문자열)에 맞춰서 적절한 설정 객체(클래스)를 생성하는 공장입니다.
        private void CreateParametersForAlgorithm(string algoName)
        {
            // 선택된 이름에 따라 적절한 설정 클래스 생성
            switch (algoName)
            {
                case "Threshold (이진화)":
                    // 이진화 설정을 담을 그릇을 새로 만듭니다. (기본값 128 등 포함)
                    CurrentParameters = new ThresholdParams();
                    break;
                case "Adaptive Threshold (적응형 이진화)":
                    CurrentParameters = new AdaptiveThresholdParams();
                    break;
                case "Morphology (모폴로지)":
                    CurrentParameters = new MorphologyParams();
                    break;
                case "Edge Detection (엣지 검출)":
                    CurrentParameters = new EdgeParams();
                    break;
                default:
                    CurrentParameters = null; // 설정이 필요 없는 경우
                    break;
            }
        }


        // 상황에 따라 원본을 보여줄지, 처리된 결과를 보여줄지 결정하는 '교통정리' 함수입니다.
        private void UpdateDisplay()
        {
            if (ShowOriginal)
                // 체크박스가 켜져있으면 -> MilService에게 "원본 내놔"라고 함
                DisplayImage = _milService.GetOriginalImage();
            else
                // 꺼져있으면 -> "결과물 내놔"라고 함
                DisplayImage = _milService.GetProcessedImage();
        }

        // --- Commands ---
        // 버튼과 연결되는 끈(Command)입니다.
        public ICommand LoadImageCommand => new RelayCommand(LoadImage);
        public ICommand ApplyAlgorithmCommand => new RelayCommand(ApplyAlgorithm);

        // [파일 열기] 버튼을 눌렀을 때
        private void LoadImage(object obj)
        {
            // 윈도우 파일 탐색기를 엽니다.
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.bmp;*.jpg;*.png" };
            // 파일을 선택하고 확인을 눌렀다면
            if (dlg.ShowDialog() == true)
            {
                // 1. MilService에게 파일을 로드하라고 시킵니다.
                _milService.LoadImage(dlg.FileName);
                // 2. 막 로드했으니 사용자가 원본을 확인하도록 "원본 보기"를 켭니다.
                ShowOriginal = true; // 로드 직후엔 원본 보여주기
                // 3. 화면 갱신
                UpdateDisplay();
            }
        }

        // [적용] 버튼을 눌렀을 때
        private void ApplyAlgorithm(object obj)
        {
            // 알고리즘 선택을 안 했으면 아무것도 안 함
            if (string.IsNullOrEmpty(SelectedAlgorithm)) return;

            // [중요] MilService에게 "이 알고리즘으로, 이 설정값(CurrentParameters)을 써서 처리해줘!"라고 명령합니다.
            // 여기서 사용자가 슬라이더로 조정한 값들이 MilService로 넘어갑니다.
            // 현재 설정된 파라미터(_currentParameters)를 넘겨줌
            _milService.ProcessImage(SelectedAlgorithm, CurrentParameters);

            // 처리가 끝났으니 결과를 보여주기 위해 "원본 보기"를 끕니다.
            ShowOriginal = false; // 적용 후엔 결과 보기로 자동 전환
            // 화면 갱신 (결과 이미지가 뜸)
            UpdateDisplay();
        }

        // 프로그램 종료 시 호출되어 메모리를 청소합니다.
        public void Cleanup() => _milService.Cleanup();

        // MVVM 패턴의 필수 요소: "값 변했음" 알림 방송국 구현
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // 간단한 Command 클래스
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public RelayCommand(Action<object> execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged;
    }
}
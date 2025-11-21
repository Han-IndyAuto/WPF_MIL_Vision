using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndyVision
{
    /// <summary>
    /// abstract: 이 클래스로는 직접 객체를 만들 수 없고, 상속받아서만 써야 한다는 뜻입니다
    /// </summary>
    public abstract class AlgorithmParamsBase: INotifyPropertyChanged
    {
        // 1. 이벤트 정의 (방송국)
        // 값이 바뀔 때마다 "나 바뀌었어!" 하고 구독자(WPF UI)들에게 신호를 보낼 이벤트입니다.
        public event PropertyChangedEventHandler PropertyChanged;

        // 2. 이벤트 발생 함수 (방송 송출 버튼)
        // [CallerMemberName]: 이 함수를 호출한 속성의 이름(예: "ThresholdValue")을 자동으로 가져옵니다.
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // 누군가 이 변수를 보고 있다면(Invoke), 알림을 보냅니다.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class ThresholdParams : AlgorithmParamsBase
    {
        // 1. Backing Field (실제 금고)
        // 데이터를 실제로 저장하는 'private' 변수입니다. 외부에서 직접 못 건드립니다.
        private byte _thresholdValue = 128;
        // 2. Property (창구/출입문)
        // 외부(WPF UI)에서 접근할 수 있는 'public' 속성입니다.
        public byte ThresholdValue
        {
            // 값을 달라고 하면 금고(_thresholdValue)에서 꺼내줍니다.
            get => _thresholdValue;

            // 값을 넣으려고 할 때 (예: 사용자가 슬라이더를 움직임)
            set
            {
                // 1. 값이 바뀔 때만 동작 (같은 값이면 무시 - 성능 최적화)
                if (_thresholdValue != value)
                {
                    // 금고에 새 값을 저장하고
                    _thresholdValue = value;

                    // 2. [중요] "ThresholdValue가 변했습니다!"라고 방송합니다.
                    OnPropertyChanged();
                }
            }
        }

        // [추가] 2. 상한값 (Max) - 기본값 255 (완전 흰색 포함)
        private byte _thresholdMax = 255;
        public byte ThresholdMax
        {
            get => _thresholdMax;
            set { if (_thresholdMax != value) { _thresholdMax = value; OnPropertyChanged(); } }
        }

    }

    public class MorphologyParams : AlgorithmParamsBase
    {
        private int _iterations = 1;
        public int Iterations
        {
            get => _iterations;
            set
            {
                if (_iterations != value)
                {
                    _iterations = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _kernelSize = 3;
        public int KernelSize
        {
            get => _kernelSize;
            set
            {
                if (_kernelSize == value)
                    return;
                _kernelSize = value;
                OnPropertyChanged();
            }
        }

        private string _operationMode = "Erode";
        public string OperationMode
        {
            get => _operationMode;
            set
            {
                if (_operationMode != value)
                {
                    _operationMode = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    public class EdgeParams : AlgorithmParamsBase
    {
        // 1. 엣지 검출 방법 (Sobel, Prewitt, Laplacian)
        private string _method = "Sobel";
        public string Method
        {
            get => _method;
            set
            {
                if (_method != value)
                {
                    _method = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _smoothness = 25;
        public int Smoothness
        {
            get => _smoothness;
            set
            {
                if (_smoothness != value)
                {
                    _smoothness = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    public class AdaptiveThresholdParams : AlgorithmParamsBase
    {
        // 1. Window Size (주변을 얼마나 넓게 볼지)
        private int _windowSize = 35;
        public int WindowSize
        {
            get => _windowSize;
            set
            {
                if (_windowSize != value)
                {
                    _windowSize = value;
                    OnPropertyChanged();
                }
            }
        }

        // 2. Offset (민감도: 값이 클수록 엄격하게 검사하여 노이즈 제거)
        private int _offset = 10;
        public int Offset
        {
            get => _offset;
            set
            {
                if (_offset != value)
                {
                    _offset = value;
                    OnPropertyChanged();
                }
            }
        }

        // 3. Mode (밝은것? 어두운것?)
        private string _mode = "Bright Ojbect (밝은 물체)";
        public string Mode
        {
            get => _mode;
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    public class BlobParams : AlgorithmParamsBase
    {
        // [추가] 1. 밝기 범위 설정 (이진화용)
        private byte _thresholdMin = 50;
        public byte ThresholdMin
        {
            get => _thresholdMin;
            set { if (_thresholdMin != value) { _thresholdMin = value; OnPropertyChanged(); } }
        }

        private byte _thresholdMax = 200;
        public byte ThresholdMax
        {
            get => _thresholdMax;
            set { if (_thresholdMax != value) { _thresholdMax = value; OnPropertyChanged(); } }
        }

        // 최소 면적(픽셀 수): 이 값보다 작은 덩어리는 무시.
        private int _minArea = 100;
        public int MinArea
        {
            get => _minArea;
            set
            {
                if (_minArea != value)
                {
                    _minArea = value;
                    OnPropertyChanged();
                }
            }
        }

        // (선택 사항) 결과를 화면에 사각형으로 그릴지 여부.
        private bool _drawBox = true;
        public bool DrawBox
        {
            get => _drawBox;
            set
            {
                if (_drawBox != value)
                {
                    _drawBox = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    public class RoiParams : AlgorithmParamsBase
    {
        // empty for future use
    }

    public class GmfParams : AlgorithmParamsBase
    {
        // 모델 정의용 파라미터
        // 윤관선을 얼마나 부드럽게 처리 할지(0 ~ 100). 높은 값일수록 자잘한 엣지는 무시.
        private double _smoothness = 50;
        public double Smoothness
        {
            get => _smoothness;
            set
            {
                if (_smoothness == value) return;
                _smoothness = value;
                OnPropertyChanged();
            }
        }

        // 검색용 파라미터 (실행 단계)
        // 최소 일치률 (0 ~ 100). 이 점수 이상인것만 찾는다.
        private double _minScore = 60;
        public double MinScore
        {
            get => _minScore;
            set
            {
                if (_minScore == value) return;
                _minScore = value;
                OnPropertyChanged();
            }
        }
    }


}

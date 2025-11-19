using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Matrox.MatroxImagingLibrary; // MIL 참조

//using System.Runtime.InteropServices;   // DllImport용


/****************************************************************************************************************
 * [필독] MIL 라이브러리 개발 시 반드시 따라야 하는 규칙 (Golden Rules)
 * MIL은 C#의 Garbage Collector(자동 메모리 관리)가 관리해주지 않는 **비관리 메모리(Unmanaged Memory)**를 사용합니다. 
 * 그래서 아래 규칙을 어기면 **메모리 누수(Memory Leak)**가 발생하여, 며칠 뒤에 장비가 멈춰버립니다.
 * 
 * 규칙 1: MbufAlloc이 있으면 반드시 MbufFree가 있어야 한다. (짝 맞추기)
 *  이미지를 로드하거나(MbufRestore), 공간을 만들 때(MbufAlloc) 생성된 ID는, 사용이 끝나면 반드시 MbufFree()로 지워야 합니다.
 *  LoadImage 함수 시작 부분에 FreeImages()를 호출하는 이유가 바로 이것입니다. 새 이미지를 펴기 전에 헌 이미지를 치우는 것입니다.
 *  
 * 규칙 2: 해제는 할당의 역순으로 (계층 구조 준수)
 *  MIL 객체는 부모-자식 관계가 있습니다. 부모(System)를 먼저 죽이면, 자식(Buffer)들이 갈 곳을 잃어 메모리 고아(Leak)가 됩니다.
 *  할당: Application -> System -> Buffer (Image)
 *  해제: Buffer -> System -> Application
 *  
 * 규칙 3: ID는 MIL_INT 또는 MIL_ID 타입을 사용한다.
 *  C#에서는 보통 long과 호환되지만, 정확성을 위해 MIL 함수에서 요구하는 타입(MIL_ID)을 사용하는 것이 좋습니다.
 *  초기값은 항상 MIL.M_NULL로 설정하여, 이 변수가 할당되었는지 안 되었는지 구분해야 합니다.
 *  
 * 규칙 4: 밴드(Channel) 수를 항상 확인한다.
 *  항상 MbufInquire(..., MIL.M_SIZE_BAND, ...)로 밴드 수를 확인하고, 내 프로그램이 처리할 수 있는 포맷(보통 1밴드 Gray)으로 변환하는 로직을 넣어야 합니다.
 *  
 * 규칙 5: MbufGet (데이터 가져오기)은 최소화한다.
 *  MbufGet은 MIL 메모리(보통 하드웨어에 가까운 고속 메모리)에서 C# 배열(일반 메모리)로 데이터를 복사해오는 명령입니다.
 *  이 과정은 느립니다. 알고리즘 처리는 가능한 Mim... 함수들을 사용해 MIL 내부에서 끝내고, 화면에 보여줄 때만 MbufGet을 사용하세요.
 *  
 *  
 ****************************************************************************************************************/

namespace IndyVision
{
    public class MilService
    {
        // 변수 선언부 : MIL의 신분증 (ID) 역할을 하는 MIL_ID 타입 변수들
        // MIL_ID: MIL에서는 모든 것(앱, 시스템, 이미지 등)을 'ID 번호'로 관리합니다.
        // 객체 자체가 아니라 "메모리 주소를 가리키는 번호표"라고 생각하면 됩니다.
        private MIL_ID MilApplication = MIL.M_NULL;     // MIL 라이브러리 전체를 총괄하는 ID
        private MIL_ID MilSystem = MIL.M_NULL;          // 장치(PC 또는 프레임그레버)를 담당하는 ID

        // 이미지를 담을 그릇 (버퍼)
        private MIL_ID MilSourceImage = MIL.M_NULL;     // 원본 보관용 (훼손 방지)
        private MIL_ID MilDestImage = MIL.M_NULL;       // 처리 결과용

        // 화면 표시용 캐시
        private ImageSource _cachedOriginal;
        private ImageSource _cachedProcessed;

        public MilService()
        {
            InitializeMIL();
        }

        private void InitializeMIL()
        {
            // 1. MappAlloc: MIL 사무소를 엽니다. (가장 먼저 해야 함)
            // MilApplication 변수를 통해 MIL 출입증을 발급 받습니다.
            MIL.MappAlloc(MIL.M_NULL, MIL.M_DEFAULT, ref MilApplication);

            // [중요] 에러가 났을 때 검은 창(Console)에 글씨가 뜨지 않게 막습니다.
            // 이걸 안 막으면 에러 날 때마다 팝업이나 콘솔이 떠서 방해됩니다.
            // MIL application의 전역 설정(옵션)을 변경하는 함수.
            MIL.MappControl(MilApplication, MIL.M_ERROR, MIL.M_PRINT_DISABLE);

            // 2. MsysAlloc: 어떤 장비를 쓸지 결정합니다.
            // "M_SYSTEM_HOST": 별도 보드 없이 PC의 CPU/RAM을 쓰겠다는 뜻입니다.
            // 만약 Matrox Solios 보드를 쓴다면 "M_SYSTEM_SOLIOS"로 바뀝니다.
            MIL.MsysAlloc(MIL.M_DEFAULT, "M_SYSTEM_HOST", MIL.M_DEFAULT, MIL.M_DEFAULT, ref MilSystem);
        }

        /// <summary>
        /// 이 부분은 프로그램이 죽지 않게 하기 위한 방어 운전 코드
        /// </summary>
        /// <param name="filePath"></param>
        /// <exception cref="Exception"></exception>
        public void LoadImage(string filePath)
        {
            // 새 이미지를 로드하기 전에, 쓰던 그릇들은 깨끗이 설거지(해제)합니다.
            FreeImages(); // 기존 이미지 메모리 해제

            MIL_ID milTempImage = MIL.M_NULL;

            try
            {
                // 1. 파일 로드: 하드디스크의 이미지를 일단 RAM(임시 그릇)으로 가져옵니다.
                // 1. 일단 파일을 임시 버퍼에 로드합니다. (컬러일 수도, 흑백일 수도 있음)
                MIL.MbufRestore(filePath, MilSystem, ref milTempImage);

                // 2. 정보 캐기: 가져온 이미지의 가로, 세로, 색상 채널 수(밴드)를 물어봅니다.
                // 2. 이미지의 크기(Width, Height)를 확인합니다.
                long width = 0, height = 0, bands = 0;
                MIL.MbufInquire(milTempImage, MIL.M_SIZE_X, ref width);
                MIL.MbufInquire(milTempImage, MIL.M_SIZE_Y, ref height);
                MIL.MbufInquire(milTempImage, MIL.M_SIZE_BAND, ref bands); // 밴드 수(1=흑백, 3=컬러)


                // 3. 진짜 그릇 만들기: 우리는 처리를 위해 무조건 '8비트 흑백' 그릇을 만듭니다.
                // M_IMAGE + M_DISP + M_PROC: "이미지 저장하고(IMG), 화면에 뿌리고(DISP), 가공도 할 거야(PROC)"라는 속성 부여
                // 3. 우리가 사용할 '진짜' 버퍼는 무조건 흑백(1 Band)으로 할당합니다.
                // M_IMAGE + M_PROC : 처리 가능한 이미지 속성 부여
                MIL.MbufAlloc2d(MilSystem, width, height, 8 + MIL.M_UNSIGNED, MIL.M_IMAGE + MIL.M_DISP + MIL.M_PROC, ref MilSourceImage);
                MIL.MbufAlloc2d(MilSystem, width, height, 8 + MIL.M_UNSIGNED, MIL.M_IMAGE + MIL.M_DISP + MIL.M_PROC, ref MilDestImage);

                // 4. 데이터 옮겨 담기 (컬러 대응)
                // 4. 컬러인지 흑백인지에 따라 다르게 복사합니다.
                if (bands == 3) // 만약 불러온 게 컬러(RGB)라면?
                {
                    // 컬러 -> 흑백 변환해서 원본 그릇에 담아라.
                    // 컬러(RGB)라면 -> 흑백(Gray)으로 변환해서 복사 (MimConvert 사용)
                    MIL.MimConvert(milTempImage, MilSourceImage, MIL.M_RGB_TO_L);
                }
                else
                {
                    // 이미 흑백이라면 -> 그냥 복사
                    MIL.MbufCopy(milTempImage, MilSourceImage);
                }

                // 5. 임시로 썼던 버퍼는 이제 필요 없으니 해제
                MIL.MbufFree(milTempImage);

                // 6. 작업용 그릇 초기화: 원본을 작업용 그릇에 복사해 둡니다.
                // 6. 결과 버퍼 초기화 (원본 복사)
                MIL.MbufCopy(MilSourceImage, MilDestImage);

                // 7. 화면 표시용 비트맵 생성 (이제 무조건 1 Band이므로 안전함)
                _cachedOriginal = ConvertMilToBitmap(MilSourceImage);
                _cachedProcessed = ConvertMilToBitmap(MilDestImage);
            }
            catch (Exception ex)
            {
                // 에러 발생 시 임시 버퍼가 남아있으면 정리
                if (milTempImage != MIL.M_NULL) MIL.MbufFree(milTempImage);
                throw new Exception("이미지 로드 중 오류 발생: " + ex.Message);
            }
        }

        // 파라미터를 받는 처리 함수
        public string ProcessImage(string algorithm, AlgorithmParamsBase parameters)
        {
            if (MilSourceImage == MIL.M_NULL) return "이미지 없음.";

            string resultMessage = "Processing Complete"; // 반환할 메시지

            // 1. 리셋: 작업용 그릇(Dest)을 다시 원본(Source)으로 덮어씁니다.
            // 이걸 안 하면, 이진화된 거 위에 또 이진화를 하게 되어 이미지가 망가집니다.
            // 항상 원본에서 시작 (누적 처리가 아닐 경우)
            MIL.MbufCopy(MilSourceImage, MilDestImage);

            // 2. 알고리즘별 처리
            switch (algorithm)
            {
                case "Threshold (이진화)":
                    if (parameters is ThresholdParams thParams)
                    {
                        // 설정된 임계값 적용
                        // MimBinarize: 픽셀 값이 기준(ThresholdValue)보다 크면 흰색(255), 작으면 검은색(0)으로 만듦
                        //MIL.MimBinarize(MilDestImage, MilDestImage, MIL.M_FIXED + MIL.M_GREATER, thParams.ThresholdValue, MIL.M_NULL);

                        // [수정] M_IN_RANGE: Min ~Max 사이의 밝기를 가진 픽셀만 찾음
                        // 파라미터 순서: 소스, 타겟, 모드, 하한값(Min), 상한값(Max)
                        MIL.MimBinarize(MilDestImage, MilDestImage, MIL.M_IN_RANGE, thParams.ThresholdValue, thParams.ThresholdMax);
                    }
                    break;

                case "Morphology (모폴로지)":
                    if (parameters is MorphologyParams morParams)
                    {
                        /*
                        // 1. 구조 요소(커널) ID 선언
                        MIL_ID MilStructElem = MIL.M_NULL;
                        // 2. 구조 요소 할당 (MstructAlloc)
                        // M_RECTANGLE: 사각형 모양 커널
                        // M_FILLED: 커널 내부가 꽉 찬 형태 (보통 이걸 사용)
                        MstructAlloc(MilSystem, MIL.M_RECTANGLE, MIL.M_FILLED, ref MilStructElem);

                        // 3. Kernel 크기 설정 (MstructControl)
                        MstructControl(MilStructElem, MIL.M_SIZE_X, morParams.KernelSize);
                        MstructControl(MilStructElem, MIL.M_SIZE_Y, morParams.KernelSize);

                        // 4. Morphology 연산 수행 (MimErode 또는 MimDilate)
                        long mode = morParams.OperationMode == "Erosion (침식)" ? MIL.M_ERODE : MIL.M_DILATE;

                        MimMorphic(MilDestImage, MilDestImage, mode, MilStructElem, morParams.Iterations);

                        // 5. 구조 요소 해제
                        MstructFree(MilStructElem);
                        */

                        // MIL.M_BIMODAL: MIL이 알아서 적절한 임계값을 찾아주는 오토 모드
                        MIL.MimBinarize(MilDestImage, MilDestImage, MIL.M_BIMODAL + MIL.M_GREATER, 0, MIL.M_NULL); // 이진화로 확실히 바이너리 이미지로 만듦

                        //MIL.MimBinarizeAdaptive(MilDestImage, MilDestImage, MIL.M_MEAN, morParams.KernelSize, MIL.M_DEFAULT, MIL.M_DEFAULT, MIL.M_NULL); // 노이즈 제거용

                        int kernelLoops = (morParams.KernelSize - 1) / 2;
                        if(kernelLoops < 1) kernelLoops = 1;

                        long totalIterations = (long)(morParams.Iterations * kernelLoops);

                        if(morParams.OperationMode == "Erode")
                        {
                            MIL.MimErode(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                        }
                        else if(morParams.OperationMode == "Dilate") // Dilation
                        {
                            MIL.MimDilate(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                        }
                        else if(morParams.OperationMode == "Open") // Opening
                        {
                            MIL.MimOpen(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                            //MIL.MimErode(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                            //MIL.MimDilate(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                        }
                        else if(morParams.OperationMode == "Close") // Closing
                        {
                            MIL.MimClose(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                            //MIL.MimDilate(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                            //MIL.MimErode(MilDestImage, MilDestImage, totalIterations, MIL.M_BINARY);
                        }



                        // 예: 설정된 횟수만큼 Erode 수행
                        //MIL.MimErode(MilDestImage, MilDestImage, morParams.Iterations, MIL.M_BINARY);
                    }
                    break;

                case "Edge Detection (엣지 검출)":
                    if (parameters is EdgeParams edgeParams)
                    {
                        // 1. 선택된 알고리즘에 따라 커널(필터) 결정.
                        long kernel = MIL.M_EDGE_DETECT; // 기본값: Sobel

                        if (edgeParams.Method.Contains("Prewitt"))
                        {
                            kernel = MIL.M_EDGE_DETECT_PREWITT_FAST;
                            //kernel = MIL.M_PREWITT;
                        }
                        else if (edgeParams.Method.Contains("Laplacian"))
                        {
                            kernel = MIL.M_LAPLACIAN_EDGE;
                        }
                        else
                        {
                            kernel = MIL.M_EDGE_DETECT; // Sobel
                        }

                        // 2. 엣지 검출 수행
                        MIL.MimConvolve(MilDestImage, MilDestImage, kernel);


                        // 1. 엣지 검출 (Convolution - Sobel 필터)
                        // MIL.M_EDGE_DETECT: 엣지가 있는 부분은 밝게, 없는 부분은 어둡게 만듭니다.
                        //MIL.MimConvolve(MilDestImage, MilDestImage, MIL.M_EDGE_DETECT);

                        // 2. (옵션) 결과 다듬기 - 파라미터 활용
                        // 사용자가 슬라이더로 설정한 값보다 '약한 엣지'는 제거(검은색)하고 
                        // '강한 엣지'만 흰색으로 남겨서 선명하게 보여줍니다.
                        // (edgeParams.Smoothness 값을 Threshold(임계값) 용도로 사용한다고 가정)
                        if (edgeParams.Smoothness > 0)
                        {
                            // 설정값보다 밝은(강한) 엣지만 남김 (이진화 처리)
                            // 값이 너무 높으면 엣지가 다 사라질 수 있으니 UI 슬라이더는 0~100 정도가 적당합니다.
                            MIL.MimBinarize(MilDestImage, MilDestImage, MIL.M_FIXED + MIL.M_GREATER, edgeParams.Smoothness, MIL.M_NULL);
                        }
                    }
                    break;

                case "Adaptive Threshold (적응형 이진화)":
                    if (parameters is AdaptiveThresholdParams adaptParams)
                    {
                        // 1. 모드 결정 (밝은 물체/어두운 물체)
                        long condition = adaptParams.Mode.Contains("Bright") ? MIL.M_GREATER : MIL.M_LESS;

                        // 2. adaptive Binarization 수행
                        // M_MEAN : 커널 내 픽셀들의 평균값을 기준으로 임계값 결정
                        // Window Size: 주변을 얼마나 넓게 볼지 결정 (홀수 권장, 예: 15, 21, 31)
                        // offset: 평균보다 얼마나 더 차이가 나야 인정할지
                        MIL.MimBinarizeAdaptive(
                            MilDestImage,           // 원본 이미지 (Copy 된 Dest)
                            MilDestImage,           // 결과 이미지 (Dest)
                            MIL.M_MEAN,             // 기준 계산법 (평균)
                            condition,              // 조건 (크거나/작거나)
                            adaptParams.WindowSize, // 지역 범위 (param1)
                            adaptParams.Offset,     // 민감도 (param2)
                            MIL.M_NULL              // 추가 옵션 없음
                            );
                    }
                    break;

                // --------------------------------------------------------------------------
                // [수정] 블롭 분석 로직 교체 (MimCalculate 대신 C# 직접 계산 사용)
                // --------------------------------------------------------------------------
                case "Blob Analysis (블롭 분석)":
                    if (parameters is BlobParams blobParams)
                    {
                        // 1. 이진화 (Binarize)
                        // 사용자가 설정한 범위(Min~Max)를 흰색(255)으로, 나머지는 검은색(0)으로
                        MIL.MimBinarize(MilDestImage, MilDestImage, MIL.M_IN_RANGE, blobParams.ThresholdMin, blobParams.ThresholdMax);

                        MIL_ID MilLabelImg = MIL.M_NULL;
                        long width = 0, height = 0;
                        MIL.MbufInquire(MilDestImage, MIL.M_SIZE_X, ref width);
                        MIL.MbufInquire(MilDestImage, MIL.M_SIZE_Y, ref height);

                        try
                        {
                            // 2. 라벨 이미지 할당 (16비트)
                            // MimLabel 결과를 담을 그릇을 만듭니다.
                            MIL.MbufAlloc2d(MilSystem, width, height, 16 + MIL.M_UNSIGNED, MIL.M_IMAGE + MIL.M_PROC, ref MilLabelImg);
                            MIL.MbufClear(MilLabelImg, 0); // 초기화

                            // 3. 라벨링 수행 (MimLabel)
                            // 연결된 픽셀끼리 같은 번호를 매깁니다. (1, 2, 3...)
                            MIL.MimLabel(MilDestImage, MilLabelImg, MIL.M_DEFAULT);

                            // 4. 라벨 데이터를 C# 배열로 가져오기 (가장 확실한 방법)
                            ushort[] labelData = new ushort[width * height];
                            MIL.MbufGet(MilLabelImg, labelData);

                            // 5. C#에서 직접 면적 및 박스 계산 (속도 빠름)
                            // Dictionary: <라벨번호, 블롭정보>
                            System.Collections.Generic.Dictionary<int, BlobInfo> blobs = new System.Collections.Generic.Dictionary<int, BlobInfo>();

                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int index = (int)(y * width + x);
                                    int label = labelData[index];

                                    if (label > 0) // 0은 배경
                                    {
                                        if (!blobs.ContainsKey(label))
                                        {
                                            // 새로운 블롭 발견 -> 등록
                                            blobs[label] = new BlobInfo { MinX = x, MaxX = x, MinY = y, MaxY = y, Area = 1, SumX = x, SumY = y };
                                        }
                                        else
                                        {
                                            // 기존 블롭 -> 정보 갱신
                                            BlobInfo info = blobs[label];
                                            info.Area++;

                                            info.SumX += x; // X 좌표 누적
                                            info.SumY += y; // Y 좌표 누적

                                            if (x < info.MinX) info.MinX = x;
                                            if (x > info.MaxX) info.MaxX = x;
                                            if (y < info.MinY) info.MinY = y;
                                            if (y > info.MaxY) info.MaxY = y;
                                        }
                                    }
                                }
                            }

                            // ------------------------------------------------------------
                            // 6. 결과 그리기 및 카운트 (컬러 박스 적용)
                            // ------------------------------------------------------------

                            // [1] 화면 표시용 '컬러 버퍼' 생성 (가로 x 세로 x 3밴드)
                            // M_PACKED: RGB 데이터가 섞여 있는 포맷 (WPF 호환용)
                            MIL_ID MilColorDisplay = MIL.M_NULL;
                            MIL.MbufAllocColor(MilSystem, 3, width, height, 8 + MIL.M_UNSIGNED, MIL.M_IMAGE + MIL.M_PROC + MIL.M_PACKED, ref MilColorDisplay);

                            // [2] 흑백 처리 결과를 컬러 버퍼로 복사 (여전히 흑백처럼 보임)
                            MIL.MbufCopy(MilDestImage, MilColorDisplay);

                            // [설정] 텍스트 배경을 투명하게 설정 (글자 뒤에 검은 박스 제거)
                            MIL.MgraControl(MIL.M_DEFAULT, MIL.M_BACKGROUND_MODE, MIL.M_TRANSPARENT);


                            // [3] 그리기 색상을 '빨간색(Red)'으로 설정
                            // MIL.M_COLOR_RED 상수가 없다면: MIL.M_RGB888(255, 0, 0) 사용
                            //MIL.MgraColor(MIL.M_DEFAULT, MIL.M_COLOR_RED);

                            int validCount = 0;
                            long padding = 5; // 박스 여백

                            foreach (var blob in blobs.Values)
                            {
                                if (blob.Area >= blobParams.MinArea)
                                {
                                    validCount++;
                                    if (blobParams.DrawBox)
                                    {
                                        // 좌표에 여백 추가
                                        long drawMinX = (blob.MinX > padding) ? blob.MinX - padding : 0;
                                        long drawMinY = (blob.MinY > padding) ? blob.MinY - padding : 0;
                                        long drawMaxX = (blob.MaxX + padding < width) ? blob.MaxX + padding : width - 1;
                                        long drawMaxY = (blob.MaxY + padding < height) ? blob.MaxY + padding : height - 1;

                                        // 무게 중심(Center of Gravity) 계산
                                        long centerX = blob.SumX / blob.Area;
                                        long centerY = blob.SumY / blob.Area;

                                        // [4] 컬러 버퍼(MilColorDisplay) 위에 박스 그리기
                                        //MIL.MgraRect(MIL.M_DEFAULT, MilColorDisplay, drawMinX, drawMinY, drawMaxX, drawMaxY);

                                        // B. 빨간색 사각형 그리기
                                        MIL.MgraColor(MIL.M_DEFAULT, MIL.M_COLOR_RED);
                                        MIL.MgraRect(MIL.M_DEFAULT, MilColorDisplay, drawMinX, drawMinY, drawMaxX, drawMaxY);

                                        // C. 파란색 점 그리기
                                        // MgraArcFill: 채워진 원 그리기 (중심X, 중심Y, 반지름X, 반지름Y, 시작각도, 종료각도)
                                        MIL.MgraColor(MIL.M_DEFAULT, MIL.M_COLOR_BLUE);
                                        MIL.MgraArcFill(MIL.M_DEFAULT, MilColorDisplay, centerX, centerY, 3, 3, 0, 360);

                                        // D. 녹색 텍스트 쓰기
                                        // 표시 내용: (X, Y, Area)
                                        string infoText = $"({centerX}, {centerY}, {blob.Area})";
                                        MIL.MgraColor(MIL.M_DEFAULT, MIL.M_COLOR_GREEN);
                                        MIL.MgraText(MIL.M_DEFAULT, MilColorDisplay, centerX + 10, centerY, infoText);
                                    }
                                }
                            }

                            // [5] 화면 갱신: 컬러 버퍼를 비트맵으로 변환하여 UI에 표시
                            _cachedProcessed = ConvertMilToBitmap(MilColorDisplay);

                            // [6] 임시 컬러 버퍼 해제 (필수)
                            MIL.MbufFree(MilColorDisplay);

                            resultMessage = $"검출 성공: {validCount}개 (전체 {blobs.Count}개 중)";

                            return resultMessage;

                        }
                        finally
                        {
                            // 메모리 해제
                            if (MilLabelImg != MIL.M_NULL) MIL.MbufFree(MilLabelImg);
                        }
                    }
                    break;

            }

            // 3. 결과 갱신: 처리된 이미지를 다시 화면용 비트맵으로 변환해 둡니다.
            // 처리 결과를 캐싱
            _cachedProcessed = ConvertMilToBitmap(MilDestImage);

            return resultMessage;
        }

        public ImageSource GetOriginalImage() => _cachedOriginal;
        public ImageSource GetProcessedImage() => _cachedProcessed;

        // (이전 코드와 동일한 ConvertMilToBitmap, FreeImages, Cleanup 함수 유지)
        private BitmapSource ConvertMilToBitmap(MIL_ID milBuffer)
        {
            if (milBuffer == MIL.M_NULL) return null;

            long width = 0, height = 0, bands = 0;

            // 1. 기본 정보 조회
            MIL.MbufInquire(milBuffer, MIL.M_SIZE_X, ref width);
            MIL.MbufInquire(milBuffer, MIL.M_SIZE_Y, ref height);
            MIL.MbufInquire(milBuffer, MIL.M_SIZE_BAND, ref bands);

            if (bands == 1) // 흑백 (Gray8)
            {
                // MIL에서 Packed 데이터 가져오기
                int milStride = (int)width;
                byte[] milData = new byte[milStride * height];
                MIL.MbufGet(milBuffer, milData);

                // WPF용 4바이트 정렬 Stride 계산
                int wpfStride = (int)((width + 3) & ~3);

                // 데이터 정렬 (밀림 방지)
                if (milStride == wpfStride)
                {
                    return BitmapSource.Create((int)width, (int)height, 96, 96, PixelFormats.Gray8, null, milData, wpfStride);
                }
                else
                {
                    byte[] wpfData = new byte[wpfStride * height];
                    for (int y = 0; y < height; y++)
                    {
                        Array.Copy(milData, y * milStride, wpfData, y * wpfStride, milStride);
                    }
                    return BitmapSource.Create((int)width, (int)height, 96, 96, PixelFormats.Gray8, null, wpfData, wpfStride);
                }
            }
            else if (bands == 3) // 컬러 (Bgr24)
            {
                // 1. 데이터 배열 준비 (너비 * 3)
                int milStride = (int)(width * 3);
                byte[] milData = new byte[milStride * height];

                // [핵심 수정] MbufGet 대신 MbufGetColor를 사용합니다.
                // MIL.M_PACKED + MIL.M_BGR24: "데이터가 어떻게 되어있든, 무조건 BGR BGR... 순서로 뭉쳐서 가져와라"
                // 이 코드가 없으면 RRR...GGG...BBB... (Planar)로 가져와서 이미지가 3개로 보입니다.
                MIL.MbufGetColor(milBuffer, MIL.M_PACKED + MIL.M_BGR24, MIL.M_ALL_BANDS, milData);

                // 2. WPF용 4바이트 정렬 Stride 계산
                int wpfStride = (int)((width * 3 + 3) & ~3);

                // 3. 데이터 정렬 (밀림 방지)
                if (milStride == wpfStride)
                {
                    return BitmapSource.Create((int)width, (int)height, 96, 96, PixelFormats.Bgr24, null, milData, wpfStride);
                }
                else
                {
                    byte[] wpfData = new byte[wpfStride * height];
                    for (int y = 0; y < height; y++)
                    {
                        Array.Copy(milData, y * milStride, wpfData, y * wpfStride, milStride);
                    }
                    return BitmapSource.Create((int)width, (int)height, 96, 96, PixelFormats.Bgr24, null, wpfData, wpfStride);
                }
            }

            return null;
        }

        private void FreeImages()
        {
            if (MilSourceImage != MIL.M_NULL) { MIL.MbufFree(MilSourceImage); MilSourceImage = MIL.M_NULL; }
            if (MilDestImage != MIL.M_NULL) { MIL.MbufFree(MilDestImage); MilDestImage = MIL.M_NULL; }
        }

        public void Cleanup()
        {
            // 할당의 '역순'으로 해제하는 것이 원칙입니다.
            FreeImages();                                                   // 1. 이미지(Buffer) 해제
            if (MilSystem != MIL.M_NULL) MIL.MsysFree(MilSystem);           // 2. 시스템 해제
            if (MilApplication != MIL.M_NULL) MIL.MappFree(MilApplication); // 3. 어플리케이션 해제
        }

        public void CropImage(int x, int y, int w, int h)
        {
            if (MilSourceImage == MIL.M_NULL) return;

            // 자식 버퍼(child buffer) 생성: 기존 이미지의 일부만 가리키는 가상의 버퍼.
            MIL_ID childBuffer = MIL.M_NULL;
            MIL.MbufChild2d(MilSourceImage, x, y, w, h, ref childBuffer);

            // 새로운 그릇 (New source)생성.
            MIL_ID newSource = MIL.M_NULL;
            MIL_ID newDest = MIL.M_NULL;

            // ROI 크기 만큼 새로 할당.
            MIL.MbufAlloc2d(MilSystem, w, h, 8 + MIL.M_UNSIGNED, MIL.M_IMAGE + MIL.M_DISP + MIL.M_PROC, ref newSource);
            MIL.MbufAlloc2d(MilSystem, w, h, 8 + MIL.M_UNSIGNED, MIL.M_IMAGE + MIL.M_DISP + MIL.M_PROC, ref newDest);

            // 데이터 복사
            MIL.MbufCopy(childBuffer, newSource);

            // 기존 버퍼 정리
            MIL.MbufFree(childBuffer);  // 자식해제
            FreeImages();               // 기존 Source와 Dest 해제.

            // 새 버퍼를 메인 버퍼로 등록.
            MilSourceImage = newSource;
            MilDestImage = newDest;
            MIL.MbufCopy(MilSourceImage, MilDestImage);

            // 화면 갱신
            _cachedOriginal = ConvertMilToBitmap(MilSourceImage);
            _cachedProcessed = ConvertMilToBitmap(MilDestImage);
        }


        // [추가] ROI 영역을 파일로 저장
        public void SaveRoiImage(string filePath, int x, int y, int w, int h)
        {
            if (MilSourceImage == MIL.M_NULL) return;

            MIL_ID childBuffer = MIL.M_NULL;
            try
            {
                // 1. 해당 영역만 가리키는 자식 버퍼 생성
                MIL.MbufChild2d(MilSourceImage, x, y, w, h, ref childBuffer);

                // 2. 파일 저장 (MbufExport)
                // 확장자에 따라 포맷 자동 결정 (MIL_M_WITH_CALIBRATION 옵션 제외 가능)
                MIL.MbufExport(filePath, MIL.M_DEFAULT, childBuffer);
            }
            finally
            {
                if (childBuffer != MIL.M_NULL) MIL.MbufFree(childBuffer);
            }
        }


    }

    // 블롭 정보를 저장할 간단한 클래스
    public class BlobInfo
    {
        public long MinX;
        public long MaxX;
        public long MinY;
        public long MaxY;
        public long Area;

        // 중심 좌표 계산을 위해 SumX, SumY 추가
        // 무게 중심(Center of Gravity) 계산용 누적값
        public long SumX;
        public long SumY;
    }
}
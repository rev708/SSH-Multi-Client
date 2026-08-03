
# SSH 탭 클라이언트

PuTTY 대신 쓰는 간단한 SSH 클라이언트입니다. 탭으로 여러 서버에 동시 접속할 수
있고, 서버 정보는 프로그램 안에 저장해두고 다음에 바로 골라 접속할 수 있습니다.

<img width="495" height="313" alt="image" src="https://github.com/user-attachments/assets/b61d287a-3258-4e94-a728-816f260c4cc0" />
<img width="413" height="269" alt="image" src="https://github.com/user-attachments/assets/5824e06a-4310-4fe8-b8df-e577a7d03406" />


## 다운로드

빌드 없이 바로 쓰고 싶다면 최신 릴리즈에서 실행 파일을 받으세요 (Windows 10/11, 64비트):

**[⬇ SshTabClient.exe 다운로드](https://github.com/rev708/SSH-Multi-Client/releases/latest/download/SshTabClient.exe)**

## 주요 기능

- 탭으로 여러 서버 동시 접속, "+" 탭으로 콘솔 추가, 탭의 × 로 개별 닫기
- 비밀번호 / SSH 키 파일 인증 모두 지원 (비밀번호·키 암호는 Windows DPAPI로 암호화 저장)
- 한글 표시 지원 (전용 한글 폰트 폴백 + UTF-8 디코딩)
- 마우스 드래그 선택 + 복사/붙여넣기 (Ctrl+Shift+C / Ctrl+Shift+V)
- 스크롤백 + 스크롤바 (휠, Shift+PageUp/PageDown)
- 글자 크기 조절

## 요구 사항

- Windows 10/11
- .NET 8 SDK (https://dotnet.microsoft.com/download) 또는 Visual Studio 2022 (17.8+)



## 사용법

1. 프로그램을 실행하면 "콘솔 1" 탭이 자동으로 열립니다.
2. "서버 관리" 버튼으로 서버를 먼저 등록하세요 (이름, 호스트, 포트, 계정,
   비밀번호 또는 SSH 키 파일).
3. 콘솔 탭에서 등록한 서버를 골라 "연결"을 누르면 그 탭이 터미널이 됩니다.
4. 탭 옆 "+" 로 탭을 계속 추가해서 여러 서버에 동시 접속할 수 있습니다.
5. 탭의 × 를 누르거나 우클릭 → "닫기"로 연결을 끊고 탭을 닫을 수 있습니다.


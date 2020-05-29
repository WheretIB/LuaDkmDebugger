#include <stdlib.h>
#include <stdio.h>

#include <Windows.h>

#pragma warning(disable: 4996)

// processId loadLibraryAddress dllNameAddress
int main(int argc, char** argv)
{
	unsigned processId = strtoul(argv[1], nullptr, 10);
	uintptr_t loadLibraryAddress = _strtoui64(argv[2], nullptr, 10);
	uintptr_t dllNameAddress = _strtoui64(argv[3], nullptr, 10);

	char buf[256];
	sprintf(buf, "Attacher %s %s %s\n", argv[1], argv[2], argv[3]);
	OutputDebugStringA(buf);

	HANDLE process = OpenProcess(0x001F0FFF, false, processId);

	if(process == nullptr)
	{
		OutputDebugStringA("Failed to CreateRemoteThread\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 1;
	}

	auto thread = CreateRemoteThread(process, nullptr, 0, (DWORD(__stdcall*)(LPVOID))loadLibraryAddress, (void*)dllNameAddress, 0, nullptr);

	if(thread == nullptr)
	{
		OutputDebugStringA("Failed to CreateRemoteThread\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 2;
	}

	return 0;
}

#include <stdlib.h>
#include <stdio.h>

#include <AclAPI.h>
#include <Sddl.h>
#include <Windows.h>

#pragma warning(disable: 4996)

int AdjustAccessControlListForUwp(char *dllPath)
{
	// Get current Access Control List
	PACL currentAcl = 0;
	PSECURITY_DESCRIPTOR securityDescriptor = 0;

	if(unsigned errorCode = GetNamedSecurityInfoA(dllPath, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, 0, 0, &currentAcl, 0, &securityDescriptor))
	{
		OutputDebugStringA("Call to 'GetNamedSecurityInfoA' failed\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, errorCode, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 11;
	}

	// sid for all application packages
	PSID sid;

	if(ConvertStringSidToSidA("S-1-15-2-1", &sid) == 0)
	{
		OutputDebugStringA("Call to 'ConvertStringSidToSidA' failed\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 12;
	}

	EXPLICIT_ACCESS_A access = { 0 };

	access.grfAccessPermissions = GENERIC_READ | GENERIC_EXECUTE;
	access.grfAccessMode = SET_ACCESS;
	access.grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
	access.Trustee.TrusteeForm = TRUSTEE_IS_SID;
	access.Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
	access.Trustee.ptstrName = (LPSTR)sid;

	// Set new access entry in the Access Control List
	PACL updatedAcl = 0;

	if(unsigned errorCode = SetEntriesInAclA(1, &access, currentAcl, &updatedAcl))
	{
		OutputDebugStringA("Call to 'SetEntriesInAclA' failed\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 13;
	}

	// Set new Access Control List
	if(unsigned errorCode = SetNamedSecurityInfoA((LPSTR)dllPath, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, 0, 0, updatedAcl, 0))
	{
		OutputDebugStringA("Call to 'SetNamedSecurityInfoA' failed\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 14;
	}

	if(securityDescriptor)
		LocalFree((HLOCAL)securityDescriptor);

	if(updatedAcl)
		LocalFree((HLOCAL)updatedAcl);

	return 0;
}

// processId loadLibraryAddress dllNameAddress dllPath
int main(int argc, char** argv)
{
	unsigned processId = strtoul(argv[1], nullptr, 10);
	uintptr_t loadLibraryAddress = _strtoui64(argv[2], nullptr, 10);
	uintptr_t dllNameAddress = _strtoui64(argv[3], nullptr, 10);
	char *dllPath = argv[4];

	char buf[256];
	sprintf(buf, "Attacher %s %s %s\n", argv[1], argv[2], argv[3]);
	OutputDebugStringA(buf);

	HANDLE process = OpenProcess(0x001F0FFF, false, processId);

	if(process == nullptr)
	{
		OutputDebugStringA("Call to 'OpenProcess' failed\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 1;
	}

	// To inject dll into sandboxed UWP process, our dll has to have read&Execute permissions in 'All Application Packages' group
	AdjustAccessControlListForUwp(dllPath);

	auto thread = CreateRemoteThread(process, nullptr, 0, (DWORD(__stdcall*)(LPVOID))loadLibraryAddress, (void*)dllNameAddress, 0, nullptr);

	if(thread == nullptr)
	{
		OutputDebugStringA("Call to 'CreateRemoteThread' failed\n");

		char* msgBuf = nullptr;
		FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, GetLastError(), MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (char*)&msgBuf, 0, NULL);
		OutputDebugStringA(msgBuf);

		return 2;
	}

	return 0;
}

/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation.
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A
 * copy of the license can be found in the License.html file at the root of this distribution. If
 * you cannot locate the Apache License, Version 2.0, please send an email to
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

 /* ****************************************************************************
  *
  * Changes: adapted for Lua
  *
  * ***************************************************************************/

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

extern "C"
{
    __declspec(dllexport) volatile char luaHelperIsInitialized;

    __declspec(dllexport) __declspec(noinline) void OnLuaHelperInitialized()
    {
        volatile char dummy = 0;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
	switch(ul_reason_for_call)
	{
    case DLL_PROCESS_ATTACH:
        luaHelperIsInitialized = 1;
        OnLuaHelperInitialized();

        // TODO: remove
        {
            HANDLE fileHandle = CreateFileA("L:\\dev\\helper_log.txt", GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);

            if(fileHandle != INVALID_HANDLE_VALUE)
            {
                WriteFile(fileHandle, "DllMain() Initialized\n", strlen("DllMain() Initialized\n"), nullptr, nullptr);

                CloseHandle(fileHandle);
            }
        }
        break;
    default:
		break;
	}

	return TRUE;
}


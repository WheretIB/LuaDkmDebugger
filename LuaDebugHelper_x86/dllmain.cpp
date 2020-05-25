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

    __declspec(dllexport) __declspec(noinline) void OnLuaHelperBreakpointHit()
    {
        volatile char dummy = 0;
    }

    __declspec(dllexport) __declspec(noinline) void OnLuaHelperStepComplete()
    {
        volatile char dummy = 0;
    }

    __declspec(dllexport) __declspec(noinline) void OnLuaHelperStepOut()
    {
        volatile char dummy = 0;
    }

    __declspec(dllexport) __declspec(noinline) void OnLuaHelperStepIn()
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

#include "lua/src5.3/lstate.h"

#include <stdio.h>

extern "C" __declspec(dllexport) unsigned luaBreakLine = 0;
extern "C" __declspec(dllexport) unsigned luaStepOver = 0;
extern "C" __declspec(dllexport) unsigned luaStepOut = 0;
extern "C" __declspec(dllexport) unsigned luaStepInto = 0;

extern "C" __declspec(dllexport) void LuaHelperHook(lua_State *L, lua_Debug *ar)
{
    if(ar->event == LUA_HOOKCALL && luaStepInto)
        OnLuaHelperStepIn();

    if(ar->event == LUA_HOOKRET && luaStepOut)
        OnLuaHelperStepOut();

    if(ar->event == LUA_HOOKLINE && (luaStepOver || luaStepInto || luaStepOut))
        OnLuaHelperStepComplete();

    if(ar->i_ci && (ar->i_ci->func->tt_ & 0x3f) == 6)
    {
        auto proto = ((LClosure*)ar->i_ci->func->value_.gc)->p;

        const char *sourceName = (char*)proto->source + sizeof(TString);

        // TODO: match source
        if(luaBreakLine != 0 && ar->currentline == luaBreakLine)
            OnLuaHelperBreakpointHit();
    }

    

    /*if(auto ci = L->ci)
    {
        if((ci->func->tt_ & 0x3f) == 6)
        {
            auto proto = ((LClosure*)ci->func->value_.gc)->p;

            auto codeStart = proto->code;

            auto pc = ci->u.l.savedpc - codeStart;

            auto line = proto->lineinfo[pc == 0 ? 0 : pc - 1];

            //assert(ar->currentline == line);

            //printf("hook at line %d from '%s'\n", line, (char*)proto->source + sizeof(TString));

            if(luaBreakLine != 0 && line == luaBreakLine)
                OnLuaHelperBreakpointHit();
        }
    }*/

    //if(breakLike != 0 && ar->currentline == breakLike)
    //	DebugBreak();

    //printf("hook '%s' at %d from '%s'\n", ar->name ? ar->name : "unknown", ar->currentline, ar->source);
}

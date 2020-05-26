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

    __declspec(dllexport) __declspec(noinline) void OnLuaHelperStepInto()
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
        break;
    default:
		break;
	}

	return TRUE;
}

#include "lua/src5.3/lstate.h"

#include <stdio.h>

struct LuaHelperBreakData
{
    uintptr_t line;
    uintptr_t proto;
};

extern "C" __declspec(dllexport) unsigned luaHelperBreakCount = 0;
extern "C" __declspec(dllexport) LuaHelperBreakData luaHelperBreakData[256] = {};
extern "C" __declspec(dllexport) unsigned luaHelperBreakHitId = 0;

extern "C" __declspec(dllexport) unsigned luaHelperStepOver = 0;
extern "C" __declspec(dllexport) unsigned luaHelperStepInto = 0;
extern "C" __declspec(dllexport) unsigned luaHelperStepOut = 0;
extern "C" __declspec(dllexport) unsigned luaHelperSkipDepth = 0;

extern "C" __declspec(dllexport) void LuaHelperHook(lua_State *L, lua_Debug *ar)
{
    if(ar->event == LUA_HOOKCALL)
    {
        if(luaHelperStepInto)
            OnLuaHelperStepInto();
        else if(luaHelperStepOver)
            luaHelperSkipDepth++;
    }

    if(ar->event == LUA_HOOKTAILCALL)
    {
        if(luaHelperStepInto)
            OnLuaHelperStepInto();
    }

    if(ar->event == LUA_HOOKRET)
    {
        if(luaHelperStepOut)
            OnLuaHelperStepOut();
        else if(luaHelperStepOver && luaHelperSkipDepth > 0)
            luaHelperSkipDepth--;
    }

    if(ar->event == LUA_HOOKLINE && (luaHelperStepOver || luaHelperStepInto || luaHelperStepOut) && luaHelperSkipDepth == 0)
        OnLuaHelperStepComplete();

    if(ar->i_ci && (ar->i_ci->func->tt_ & 0x3f) == 6)
    {
        auto proto = ((LClosure*)ar->i_ci->func->value_.gc)->p;

        const char *sourceName = (char*)proto->source + sizeof(TString);

        for(auto curr = luaHelperBreakData, end = luaHelperBreakData + luaHelperBreakCount; curr != end; curr++)
        {
            if(ar->currentline == curr->line && uintptr_t(proto) == curr->proto)
            {
                luaHelperBreakHitId = curr - luaHelperBreakData;

                OnLuaHelperBreakpointHit();
            }
        }
    }
}

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

    __declspec(dllexport) char luaHelperWorkingDirectory[1024] = {};

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
        GetCurrentDirectoryA(1024, luaHelperWorkingDirectory);

        luaHelperIsInitialized = 1;
        OnLuaHelperInitialized();
        break;
    default:
		break;
	}

	return TRUE;
}

#include <stdio.h>

//#define DEBUG_MODE

#define LUA_HOOKCALL	0
#define LUA_HOOKRET	1
#define LUA_HOOKLINE	2
#define LUA_HOOKCOUNT	3
#define LUA_HOOKTAILCALL 4

namespace Lua_5_3
{
    const unsigned LUA_IDSIZE = 60;

    typedef unsigned char lu_byte;

    struct GCObject;

    union Value
    {
        GCObject *gc;    /* collectable objects */
        void *p;         /* light userdata */
        int b;           /* booleans */
        void* f; /* light C functions */
        long long i;   /* integer numbers */
        double n;    /* float numbers */
    };

    struct TValue
    {
        Value value_;
        int tt_;
    };

    typedef TValue *StkId;

    typedef unsigned Instruction;

    struct LocVar;
    struct Upvaldesc;

    struct Proto;

    struct LClosure
    {
        GCObject *next;
        lu_byte tt;
        lu_byte marked;
        lu_byte nupvalues;
        GCObject *gclist;
        struct Proto *p;

        // Don't care for other fields
    };

    struct CallInfo
    {
        StkId func;  /* function index in the stack */

        // Don't care for other fields
    };

    struct lua_Debug
    {
        int event;
        const char *name;	/* (n) */
        const char *namewhat;	/* (n) 'global', 'local', 'field', 'method' */
        const char *what;	/* (S) 'Lua', 'C', 'main', 'tail' */
        const char *source;	/* (S) */
        int currentline;	/* (l) */
        int linedefined;	/* (S) */
        int lastlinedefined;	/* (S) */
        unsigned char nups;	/* (u) number of upvalues */
        unsigned char nparams;/* (u) number of parameters */
        char isvararg;        /* (u) */
        char istailcall;	/* (t) */
        char short_src[LUA_IDSIZE]; /* (S) */
        /* private part */
        struct CallInfo *i_ci;  /* active function */
    };

#if defined(DEBUG_MODE)
    struct TString
    {
        GCObject *next; lu_byte tt; lu_byte marked;
        lu_byte extra;  /* reserved words for short strings; "has hash" for longs */
        lu_byte shrlen;  /* length for short strings */
        unsigned int hash;
        union
        {
            size_t lnglen;  /* length for long strings */
            struct TString *hnext;  /* linked list for hash table */
        } u;
    };

    struct Proto
    {
        GCObject *next; lu_byte tt; lu_byte marked;
        lu_byte numparams;  /* number of fixed parameters */
        lu_byte is_vararg;
        lu_byte maxstacksize;  /* number of registers needed by this function */
        int sizeupvalues;  /* size of 'upvalues' */
        int sizek;  /* size of 'k' */
        int sizecode;
        int sizelineinfo;
        int sizep;  /* size of 'p' */
        int sizelocvars;
        int linedefined;  /* debug information  */
        int lastlinedefined;  /* debug information  */
        TValue *k;  /* constants used by the function */
        Instruction *code;  /* opcodes */
        struct Proto **p;  /* functions defined inside the function */
        int *lineinfo;  /* map from opcodes to source lines (debug information) */
        LocVar *locvars;  /* information about local variables (debug information) */
        Upvaldesc *upvalues;  /* upvalue information */
        struct LClosure *cache;  /* last-created closure with this prototype */
        TString  *source;  /* used for debug information */
        GCObject *gclist;
    };
#endif
}

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

extern "C" __declspec(dllexport) void LuaHelperHook(void *L, Lua_5_3::lua_Debug *ar)
{
#if defined(DEBUG_MODE)
    const char *sourceName = "uknown location";

    if(ar->i_ci && (ar->i_ci->func->tt_ & 0x3f) == 6)
    {
        auto proto = ((Lua_5_3::LClosure*)ar->i_ci->func->value_.gc)->p;

        sourceName = (char*)proto->source + sizeof(Lua_5_3::TString);
    }

    if(ar->event == LUA_HOOKCALL)
    {
        if(luaHelperStepInto)
        {
            printf("hook call at line %d from '%s', step into (skip depth %d)\n", ar->currentline, sourceName, luaHelperSkipDepth);

            OnLuaHelperStepInto();
        }
        else if(luaHelperStepOver || luaHelperStepOut)
        {
            printf("hook call at line %d from '%s', step over (skip depth raised to %d)\n", ar->currentline, sourceName, luaHelperSkipDepth + 1);

            luaHelperSkipDepth++;
        }
    }

    if(ar->event == LUA_HOOKTAILCALL)
    {
        if(luaHelperStepInto)
        {
            printf("hook tail at line %d from '%s', step into (skip depth %d)\n", ar->currentline, sourceName, luaHelperSkipDepth);

            OnLuaHelperStepInto();
        }
    }

    if(ar->event == LUA_HOOKRET)
    {
        if(luaHelperStepOut && luaHelperSkipDepth == 0)
        {
            printf("hook return at line %d from '%s', step out (skip depth %d)\n", ar->currentline, sourceName, luaHelperSkipDepth);

            OnLuaHelperStepOut();
        }
        else if((luaHelperStepOver || luaHelperStepOut) && luaHelperSkipDepth > 0)
        {
            printf("hook return at line %d from '%s', step over (skip depth dropped to %d)\n", ar->currentline, sourceName, luaHelperSkipDepth - 1);

            luaHelperSkipDepth--;
        }
    }

    if(ar->event == LUA_HOOKLINE && (luaHelperStepOver || luaHelperStepInto))
    {
        printf("hook line at line %d from '%s', step%s%s%s (skip depth %d)\n", ar->currentline, sourceName, luaHelperStepOver ? " over" : "", luaHelperStepInto ? " into" : "", luaHelperStepOut ? " out" : "", luaHelperSkipDepth);

        if(luaHelperSkipDepth == 0)
        {
            printf("step complete\n");

            OnLuaHelperStepComplete();
        }
    }
#else
    if(ar->event == LUA_HOOKCALL)
    {
        if(luaHelperStepInto)
        {
            OnLuaHelperStepInto();
        }
        else if(luaHelperStepOver || luaHelperStepOut)
        {
            luaHelperSkipDepth++;
        }
    }

    if(ar->event == LUA_HOOKTAILCALL)
    {
        if(luaHelperStepInto)
        {
            OnLuaHelperStepInto();
        }
    }

    if(ar->event == LUA_HOOKRET)
    {
        if(luaHelperStepOut && luaHelperSkipDepth == 0)
        {
            OnLuaHelperStepOut();
        }
        else if((luaHelperStepOver || luaHelperStepOut) && luaHelperSkipDepth > 0)
        {
            luaHelperSkipDepth--;
        }
    }

    if(ar->event == LUA_HOOKLINE && (luaHelperStepOver || luaHelperStepInto) && luaHelperSkipDepth == 0)
        OnLuaHelperStepComplete();
#endif

    if(ar->i_ci && (ar->i_ci->func->tt_ & 0x3f) == 6)
    {
        auto proto = ((Lua_5_3::LClosure*)ar->i_ci->func->value_.gc)->p;

        for(auto curr = luaHelperBreakData, end = luaHelperBreakData + luaHelperBreakCount; curr != end; curr++)
        {
            if(ar->currentline == curr->line && uintptr_t(proto) == curr->proto)
            {
                luaHelperBreakHitId = unsigned(curr - luaHelperBreakData);

                OnLuaHelperBreakpointHit();
            }
        }
    }
}

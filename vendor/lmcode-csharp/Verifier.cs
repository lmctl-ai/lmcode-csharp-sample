using System;
using System.Collections.Generic;

namespace LmCode;

public sealed partial class Program
{
    private void Verify(int haltDepth = -1, string returns = "")
    {
        var starts = new List<int>();
        var jumps = new List<(int At, int Target, bool Ordinary)>();
        var open = new List<LoopFrame>();
        var regions = new List<LoopFrame>();
        var whileExits = new List<(int At, int Target)>();
        var usedSlots = new HashSet<int>();
        var stores = new List<(int At, int Slot)>();
        var r = new Reader(code);
        var halted = false;

        static void Index(int i, int count, string what)
        {
            if ((uint)i >= (uint)count)
                throw Error.BadBytecode($"{what} index {i} out of range ({count} entries)");
        }

        while (!r.Done)
        {
            int at = r.Pos;
            starts.Add(at);
            byte raw = r.U8();
            if (!IsKnownOpcode(raw)) throw Error.BadBytecode($"unknown opcode {raw} at offset {at}");
            Op op = (Op)raw;
            halted = false;
            switch (op)
            {
                case Op.PUSH_INT: r.I64(); break;
                case Op.PUSH_FLOAT: r.F64(); break;
                case Op.PUSH_STR: Index((int)r.U32(), consts.Count, "constant"); break;
                case Op.PUSH_BOOL:
                    if (r.U8() > 1) throw Error.BadBytecode("PUSH_BOOL operand must be 0 or 1");
                    break;
                case Op.LOAD_LOCAL:
                case Op.STORE_LOCAL:
                    {
                        int slot = r.U16(); Index(slot, locals.Count, "local slot");
                        if (op == Op.STORE_LOCAL) stores.Add((at, slot));
                        break;
                    }
                case Op.LOAD_CTX: Index(r.U16(), contexts.Count, "context"); break;
                case Op.CALL_FN:
                case Op.CALL_METHOD:
                    {
                        int i = r.U16(); int argc = r.U8(); Index(i, imports.Count, "import");
                        bool method = imports[i].TypeName is not null;
                        if (op == Op.CALL_FN && method)
                            throw Error.BadBytecode($"CALL_FN at offset {at} names import {i}, which is a method");
                        if (op == Op.CALL_METHOD && !method)
                            throw Error.BadBytecode($"CALL_METHOD at offset {at} names import {i}, which is a function");
                        if (imports[i].Parameters.Length != argc)
                            throw Error.BadBytecode($"{op} at offset {at} passes {argc} argument(s), the import takes {imports[i].Parameters.Length}");
                        break;
                    }
                case Op.MATH:
                    if (r.U8() > 17) throw Error.BadBytecode($"MATH at offset {at} has unknown function {code[r.Pos - 1]}");
                    break;
                case Op.JUMP:
                case Op.JUMP_IF_FALSE:
                case Op.JUMP_IF_TRUE:
                    {
                        int rel = r.I32();
                        if (rel < 0) throw Error.BadBytecode($"jump at offset {at} goes backwards outside a range loop");
                        long target = (long)r.Pos + rel;
                        if (target < 0 || target > int.MaxValue) throw Error.BadBytecode($"jump at offset {at} has an invalid target");
                        jumps.Add((at, (int)target, true));
                        break;
                    }
                case Op.FOR_RANGE:
                    {
                        int counter = r.U16(), end = r.U16(), rel = r.I32();
                        Index(counter, locals.Count, "FOR_RANGE counter local");
                        Index(end, locals.Count, "FOR_RANGE end local");
                        if (counter == end) throw Error.BadBytecode($"FOR_RANGE at offset {at} reuses local {counter} for counter and end");
                        foreach (int slot in new[] { counter, end })
                        {
                            if (!locals[slot].Hidden || locals[slot].TypeName != "int")
                                throw Error.BadBytecode($"FOR_RANGE at offset {at} requires hidden int local {slot}, got {(locals[slot].Hidden ? "" : "visible ")}{locals[slot].TypeName}");
                            if (!usedSlots.Add(slot)) throw Error.BadBytecode($"FOR_RANGE at offset {at} reuses loop local {slot}");
                        }
                        long exit = (long)r.Pos + rel;
                        if (exit <= at || exit > int.MaxValue) throw Error.BadBytecode($"FOR_RANGE at offset {at} exits backwards");
                        var frame = new LoopFrame(at, r.Pos, counter, end, (int)exit, Op.FOR_RANGE, open.Count == 0 ? -1 : open[^1].Opener, false, 0);
                        open.Add(frame); regions.Add(frame); break;
                    }
                case Op.FOR_NEXT:
                    {
                        int end = r.U16(), rel = r.I32();
                        if (open.Count == 0 || open[^1].Op != Op.FOR_RANGE)
                            throw Error.BadBytecode($"FOR_NEXT at offset {at} has no matching FOR_RANGE");
                        var frame = open[^1]; open.RemoveAt(open.Count - 1);
                        if (end != frame.End) throw Error.BadBytecode($"FOR_NEXT at offset {at} closes the wrong range loop (end local {end}, expected {frame.End})");
                        if ((long)r.Pos + rel != frame.Body) throw Error.BadBytecode($"FOR_NEXT at offset {at} must jump to its FOR_RANGE body start {frame.Body}");
                        if (rel >= 0) throw Error.BadBytecode($"FOR_NEXT at offset {at} must jump backwards");
                        jumps.Add((at, frame.Body, false));
                        loops[at] = (frame.Counter, frame.End);
                        if (frame.Exit != r.Pos) throw Error.BadBytecode($"FOR_RANGE at offset {frame.Opener} must exit immediately after its FOR_NEXT");
                        break;
                    }
                case Op.WHILE_BEGIN:
                    open.Add(new LoopFrame(at, r.Pos, 0, 0, 0, Op.WHILE_BEGIN, open.Count == 0 ? -1 : open[^1].Opener, false, 0));
                    break;
                case Op.WHILE_TEST:
                    {
                        int rel = r.I32();
                        if (open.Count == 0 || open[^1].Op != Op.WHILE_BEGIN)
                            throw Error.BadBytecode($"WHILE_TEST at offset {at} has no matching WHILE_BEGIN");
                        var frame = open[^1];
                        if (frame.Tested) throw Error.BadBytecode($"WHILE_BEGIN at offset {frame.Opener} has more than one WHILE_TEST");
                        if (rel < 0) throw Error.BadBytecode($"WHILE_TEST at offset {at} exits backwards");
                        frame = frame with { Tested = true, Test = at, Exit = Target(r.Pos, rel) };
                        open[^1] = frame; whileExits.Add((at, frame.Exit));
                        break;
                    }
                case Op.LOOP_BACK:
                    {
                        int rel = r.I32();
                        if (open.Count == 0 || open[^1].Op != Op.WHILE_BEGIN)
                            throw Error.BadBytecode($"LOOP_BACK at offset {at} has no matching WHILE_BEGIN");
                        var frame = open[^1]; open.RemoveAt(open.Count - 1);
                        if (!frame.Tested) throw Error.BadBytecode($"LOOP_BACK at offset {at} has no matching tested WHILE_BEGIN");
                        if ((long)r.Pos + rel != frame.Opener) throw Error.BadBytecode($"LOOP_BACK at offset {at} does not target its paired WHILE_BEGIN");
                        if (frame.Exit != r.Pos) throw Error.BadBytecode($"WHILE_TEST at offset {frame.Opener} must exit immediately after its LOOP_BACK");
                        regions.Add(frame); break;
                    }
                case Op.HALT:
                    if (open.Count != 0) throw Error.BadBytecode($"HALT at offset {at} is inside loop");
                    halted = true; break;
                default: break;
            }
        }
        if (!halted) throw Error.BadBytecode("code does not end with HALT");
        if (open.Count != 0) throw Error.BadBytecode($"{(open[^1].Op == Op.FOR_RANGE ? "FOR_RANGE" : "WHILE_BEGIN")} at offset {open[^1].Opener} is not closed by {(open[^1].Op == Op.FOR_RANGE ? "FOR_NEXT" : "LOOP_BACK")}");
        foreach (var (at, slot) in stores)
            if (usedSlots.Contains(slot)) throw Error.BadBytecode($"STORE_LOCAL at offset {at} writes a read-only range slot {slot}");
        foreach (var jump in jumps)
        {
            if (!starts.Contains(jump.Target)) throw Error.BadBytecode($"jump at offset {jump.At} targets {jump.Target}, not an instruction start");
            if (jump.Ordinary)
            {
                LoopFrame? from = Region(regions, jump.At), to = Region(regions, jump.Target);
                if (from is { Op: Op.WHILE_BEGIN } && jump.At < from.Value.Test && jump.Target > from.Value.Test)
                    throw Error.BadBytecode($"jump at offset {jump.At} skips its WHILE_TEST");
                bool validBreak = from is not null && jump.Target == from.Value.Exit &&
                    ((to is null && from.Value.Parent == -1) || (to is not null && to.Value.Opener == from.Value.Parent));
                if (!Nullable.Equals(from, to) && !validBreak)
                    throw Error.BadBytecode($"jump at offset {jump.At} crosses a loop boundary");
            }
        }
        foreach (var frame in regions)
        {
            if (!starts.Contains(frame.Exit)) throw Error.BadBytecode($"FOR_RANGE at offset {frame.Opener} exits to {frame.Exit}, not an instruction start");
            var owner = Region(regions, frame.Exit);
            if ((owner is null ? -1 : owner.Value.Opener) != frame.Parent)
                throw Error.BadBytecode($"FOR_RANGE at offset {frame.Opener} exits to an invalid loop region");
        }
        foreach (var (at, target) in whileExits)
        {
            bool sourceFound = regions.Exists(f => f.Op == Op.WHILE_BEGIN && f.Test == at);
            var source = sourceFound ? regions.Find(f => f.Op == Op.WHILE_BEGIN && f.Test == at) : default;
            var owner = Region(regions, target);
            if (!starts.Contains(target) || !sourceFound || (owner is null ? -1 : owner.Value.Opener) != source.Parent)
                throw Error.BadBytecode($"WHILE_TEST at offset {at} exits to an invalid loop region");
        }
        foreach (var line in lines)
            if (line.Pc >= code.Length) throw Error.BadBytecode($"line table entry points at pc {line.Pc}, beyond the code");
        VerifyStack(haltDepth, returns);
    }

    private void VerifyStack(int haltDepth, string returns)
    {
        // The full stack walk is implemented in the same form as the reference: decode all instruction starts,
        // propagate operand depth over reachable fallthrough/branch edges, and reject underflow or mismatched joins.
        var starts = new List<int>(); var r = new Reader(code); var hasLoop = false;
        var whileHeads = new Stack<int>(); var whilePairs = new List<(int Test, int Head)>();
        while (!r.Done)
        {
            starts.Add(r.Pos); var op = (Op)r.U8(); hasLoop |= op is Op.FOR_RANGE or Op.WHILE_BEGIN;
            if (op == Op.WHILE_BEGIN) whileHeads.Push(starts[^1]);
            else if (op == Op.WHILE_TEST && whileHeads.Count != 0) whilePairs.Add((starts[^1], whileHeads.Peek()));
            else if (op == Op.LOOP_BACK && whileHeads.Count != 0) whileHeads.Pop();
            switch (op)
            {
                case Op.PUSH_INT: r.I64(); break;
                case Op.PUSH_FLOAT: r.F64(); break;
                case Op.PUSH_STR: r.U32(); break;
                case Op.PUSH_BOOL: r.U8(); break;
                case Op.LOAD_LOCAL or Op.STORE_LOCAL or Op.LOAD_CTX: r.U16(); break;
                case Op.CALL_FN or Op.CALL_METHOD: r.U16(); r.U8(); break;
                case Op.JUMP or Op.JUMP_IF_FALSE or Op.JUMP_IF_TRUE or Op.WHILE_TEST or Op.LOOP_BACK: r.I32(); break;
                case Op.FOR_RANGE: r.U16(); r.U16(); r.I32(); break;
                case Op.FOR_NEXT: r.U16(); r.I32(); break;
                case Op.MATH: r.U8(); break;
            }
        }
        if (!hasLoop && haltDepth < 0) return;
        if (starts.Count == 0) throw Error.BadBytecode("code is empty");
        var incoming = new long?[starts.Count]; var work = new Stack<int>(); incoming[0] = 0; work.Push(0);
        void Merge(int target, long depth)
        {
            int ix = starts.IndexOf(target); if (ix < 0) throw Error.BadBytecode($"control flow targets {target}, not an instruction start");
            if (incoming[ix] is null) { incoming[ix] = depth; work.Push(ix); }
            else if (incoming[ix] != depth) throw Error.BadBytecode($"inconsistent operand stack depth at control-flow join {target}: {incoming[ix]} versus {depth}");
        }
        while (work.Count != 0)
        {
            int ix = work.Pop(), pc = starts[ix]; var q = new Reader(code) { Pos = pc }; var op = (Op)q.U8(); long pop = 0, push = 0; var targets = new List<int>(); bool fall = true;
            switch (op)
            {
                case Op.PUSH_INT or Op.PUSH_FLOAT or Op.PUSH_STR or Op.PUSH_BOOL or Op.LOAD_LOCAL or Op.LOAD_CTX: push = 1; break;
                case Op.STORE_LOCAL or Op.POP: pop = 1; break;
                case Op.DUP: pop = 1; push = 2; break;
                case Op.CALL_FN: { int i = q.U16(), a = q.U8(); pop = a; push = imports[i].Returns == "void" ? 0 : 1; break; }
                case Op.CALL_METHOD: { int i = q.U16(), a = q.U8(); pop = a + 1; push = imports[i].Returns == "void" ? 0 : 1; break; }
                case Op.EQ or Op.NE or Op.ADD_I or Op.SUB_I or Op.MUL_I or Op.DIV_I or Op.MOD_I or Op.ADD_F or Op.SUB_F or Op.MUL_F or Op.DIV_F or Op.LT_I or Op.LE_I or Op.GT_I or Op.GE_I or Op.LT_F or Op.LE_F or Op.GT_F or Op.GE_F or Op.CONCAT: pop = 2; push = 1; break;
                case Op.NOT or Op.NEG_I or Op.NEG_F or Op.I2F or Op.I2S or Op.F2S or Op.B2S: pop = 1; push = 1; break;
                case Op.MATH: { int f = q.U8(); pop = f is >= 0 and <= 4 or >= 10 and <= 14 ? 1 : f is >= 5 and <= 7 or >= 15 and <= 16 ? 2 : f is 8 or 9 or 17 ? 3 : throw Error.BadBytecode($"unknown MATH function {f}"); push = 1; break; }
                case Op.JUMP: { int rel = q.I32(); targets.Add(Target(q.Pos, rel)); fall = false; break; }
                case Op.JUMP_IF_FALSE or Op.JUMP_IF_TRUE: { pop = 1; int rel = q.I32(); targets.Add(Target(q.Pos, rel)); break; }
                case Op.FOR_RANGE: q.U16(); q.U16(); { int rel = q.I32(); targets.Add(Target(q.Pos, rel)); } pop = 2; break;
                case Op.FOR_NEXT: q.U16(); { int rel = q.I32(); targets.Add(Target(q.Pos, rel)); } break;
                case Op.WHILE_TEST: { pop = 1; int rel = q.I32(); targets.Add(Target(q.Pos, rel)); break; }
                case Op.LOOP_BACK: { int rel = q.I32(); targets.Add(Target(q.Pos, rel)); fall = false; break; }
                case Op.HALT: fall = false; break;
            }
            long depth = incoming[ix]!.Value; if (depth < pop) throw Error.BadBytecode($"operand stack underflow at offset {pc}"); long after = depth - pop + push;
            if (op == Op.HALT && haltDepth >= 0 && after != haltDepth) throw Error.BadBytecode($"HALT leaves {after} values, interface returns {returns}");
            foreach (int target in targets) Merge(target, after);
            if (fall) { if (ix + 1 >= starts.Count) throw Error.BadBytecode("control flow falls past the end of code"); Merge(starts[ix + 1], after); }
        }
        foreach (var (test, head) in whilePairs)
        {
            int testIx = starts.IndexOf(test), headIx = starts.IndexOf(head);
            if (testIx >= 0 && headIx >= 0 && incoming[testIx] is long testDepth && incoming[headIx] is long headDepth && testDepth != headDepth + 1)
                throw Error.BadBytecode($"WHILE_TEST must leave the loop-head stack depth at offset {test}");
        }
    }

    private static int Target(int pos, int rel)
    {
        try { return checked(pos + rel); }
        catch (OverflowException) { throw Error.BadBytecode($"control flow target from offset {pos} is outside the code"); }
    }

    private static LoopFrame? Region(List<LoopFrame> regions, int pc)
    {
        LoopFrame? owner = null;
        foreach (var f in regions)
            if (f.Body <= pc && pc < f.Exit && (owner is null || f.Opener > owner.Value.Opener)) owner = f;
        return owner;
    }

    private static bool IsKnownOpcode(byte raw) => raw switch
    {
        0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x0A or 0x0B or
        0x0C or 0x0D or 0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x16 or
        0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x1E or 0x1F or 0x20 or 0x21 or
        0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x29 or 0x2F or 0x31 or 0x32 or
        0x33 or 0x34 or 0x35 or 0x36 => true,
        _ => false
    };

    private readonly record struct LoopFrame(int Opener, int Body, int Counter, int End, int Exit, Op Op, int Parent, bool Tested, int Test);
}

// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public enum IrOpcode : byte
{
    
    Nop = 0,
    Assign,         
    Load,           
    Store,          
    Add,            
    Sub,            
    Mul,            
    IMul,           
    Div,            
    IDiv,           
    Mod,            
    Neg,            
    And,            
    Or,             
    Xor,            
    Not,            
    Shl,            
    Shr,            
    Sar,            
    Rol,            
    Ror,            
    Cmp,            
    Test,           
    Branch,         
    CondBranch,     
    Call,           
    Return,         
    Phi,            
    ZeroExtend,     
    SignExtend,     
    Truncate,       
    StackAlloc,     
    Bswap,          
    Unknown,        
}

public enum IrCondition : byte
{
    None = 0,
    Equal,          
    NotEqual,       
    SignedLess,     
    SignedLessEq,   
    SignedGreater,  
    SignedGreaterEq,
    UnsignedBelow,  
    UnsignedBelowEq,
    UnsignedAbove,  
    UnsignedAboveEq,
    Sign,           
    NotSign,        
    Overflow,       
    NotOverflow,    
}

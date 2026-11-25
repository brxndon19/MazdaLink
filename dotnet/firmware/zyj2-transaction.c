/* SPDX-License-Identifier: GPL-3.0-or-later
 * Fixed ZYJ2 / SH7058S transaction kernel. No application-ID dependency.
 * ENABLE_FLASH=0 remains the read/staging image. ENABLE_FLASH=1 is selected
 * only by the separate original-only detached-spare qualification capability.
 * No libc, external payload code, arbitrary flash destination or block selector.
 */
typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
#ifndef ENABLE_FLASH
#define ENABLE_FLASH 0
#endif
#define B(a) (*(volatile u8 *)(a))
#define W(a) (*(volatile u16 *)(a))
#define L(a) (*(volatile u32 *)(a))
#define F 0xffffe800U
#define BUFFER ((volatile u8 *)0xffffa000U)
#define COMMAND ((volatile u8 *)0xffffa080U)
#define RESULT ((volatile u8 *)0xffffa0a0U)
#define SCRATCH ((volatile u8 *)0xffffa100U)
#define VECTORS ((volatile u32 *)0xffff6000U)
#define MAGIC 0x4d4c5431U
#define READ 0x11U
#define STAGE 0x12U
#define ERASE 0x21U
#define PROGRAM 0x22U
#define END 0x7fU
struct state {
 u32 magic, cookie, sequence, next_unit, generation, erased, tainted, initialized, busy, faults;
 u8 shadow, services, service_error, service_owned;
 u32 fpfr;
};
#define S ((volatile struct state *)0xffffa0c0U)
extern void transaction_service(void);
extern void transaction_trap(void);
extern void transaction_nmi(void);
#if ENABLE_FLASH
extern void transaction_flash_service(void);
#define CALLBACK transaction_flash_service
#define BUSY_CALLS L(0xffffa110U)
#define BUSY_SERVICES L(0xffffa114U)
#else
#define CALLBACK transaction_service
#endif
static u32 get32(const volatile u8 *p) { return ((u32)p[0]<<24)|((u32)p[1]<<16)|((u32)p[2]<<8)|p[3]; }
static u32 get16(const volatile u8 *p) { return ((u32)p[0]<<8)|p[1]; }
static void put32(volatile u8 *p,u32 v) { p[0]=v>>24;p[1]=v>>16;p[2]=v>>8;p[3]=v; }
static void put16(volatile u8 *p,u32 v) { p[0]=v>>8;p[1]=v; }
static u32 crc32(const volatile u8 *p,u32 n) {
 u32 c=0xffffffffU,k;
 while(n--) { c^=*p++;for(k=0;k<8;k++)c=(c>>1)^(0xedb88320U & (0U-(c&1))); }
 return ~c;
}
static u32 protected(void) {
 return B(F)==0 && B(F+4)==0 && B(F+5)==0 && W(0xffffec26U)==0;
}
static u32 selections_zero(void) { return B(F+1)==0 && B(F+2)==0 && B(F+6)==0; }
static u32 preflight(void) {
 u32 c;
 S->service_owned=0;S->service_error=0;
 if(((u32 (*)(void))0xee4U)()!=1)return 1;
 if(!protected() || !selections_zero())return 2;
 if(L(0x6200U)!=0x5a594333U)return 3;
 c=W(0x7000U);
 if(c>=100 || (u16)(c^W(0x7002U))!=0xffffU)return 3;
 if((W(0xffffd004U)&0xf733U)!=0xe320U || (W(0xffffd006U)&255)!=1 ||
    (W(0xffffd804U)&0xf733U)!=0xe320U || (W(0xffffd806U)&255)!=1)return 4;
 if(W(0xfffff740U)!=0 || W(0xfffff742U)!=0 || W(0xfffff744U)!=0 || (B(0xfffff784U)&1))return 5;
 if((B(0xffffec10U)&127)!=61 || B(0xffffbfadU)!=119 || B(0xffffbfa8U)!=0 ||
    !(B(0xfffff730U)&128) || (B(0xfffff732U)&192))return 6;
 c=B(0xfffff738U);
 if((c^B(0xfffff782U))&128)return 6;
 /* Explicit per-entry ownership: unknown non-output high-byte latches are
  * normalized. Other configured outputs and non-output physical pins stay put. */
 S->shadow=c;B(0xfffff738U)=c;S->service_owned=1;
 transaction_service();
 return S->service_error?6:0;
}
static void send(u32 type,u32 n,const volatile u8 *p) {
 if(S->busy)transaction_trap();
 transaction_service();
 if(type==0xc0 && (S->service_error || S->faults)) { SCRATCH[3]=20;S->tainted=1; }
 ((void (*)(u32,u32,const volatile u8 *))0x4c2U)(type,n,p);
}
static void stream(u32 type,const volatile u8 *p,u32 n) {
 u32 i;
 for(i=0;i<n/4;i++) { put16(SCRATCH,i);SCRATCH[2]=p[i*4];SCRATCH[3]=p[i*4+1];
  SCRATCH[4]=p[i*4+2];SCRATCH[5]=p[i*4+3];send(type,6,SCRATCH); }
}
static u32 cleanup(void) {
 B(F+4)=0;B(F+1)=0;B(F+2)=0;B(F+6)=0;
 return protected() && selections_zero() && !S->service_error;
}
static u32 initialize(void) {
 u32 i,r;
 for(i=0;i<256;i++)VECTORS[i]=(u32)transaction_trap;
 VECTORS[11]=(u32)transaction_nmi;
 for(i=0;i<2;i++) {
  volatile u8 *base=(volatile u8 *)(i?0xffff2000U:0xffff1000U);
  B(F+1)=i?0:1;B(F+2)=i?1:0;B(F+6)=i?4:2;*base=255;
  __asm__ volatile("ldc %0,vbr"::"r"(0U):"memory");
  B(F+4)=0xa5;B(F)=1;
  __asm__ volatile("nop\n\tnop\n\tnop\n\tnop\n\tnop\n\tnop\n\tnop\n\tnop":::"memory");
  B(F+4)=0;
  if(*base || B(F+6)!=(i?4:2) || !protected())return 7+i;
  r=((u32 (*)(u32,u32))(base+32))(8000,(u32)CALLBACK);
  S->fpfr=r;
  if(r || !protected() || S->service_error)return 9+i;
  transaction_service();
 }
 if(!cleanup())return 11;
 S->initialized=1;return 0;
}
#if ENABLE_FLASH
static u32 unit_offset(u32 v) {
 __asm__ volatile("shll2 %0\n\tshll2 %0\n\tshll2 %0\n\tshll %0":"+r"(v)::"t");return v;
}
static u32 enable(void) {
 u32 i;
 if(!protected() || !selections_zero() || (W(0xffffecb0U)&1) || S->faults || S->service_error)return 12;
 for(i=0;i<256;i++)if(VECTORS[i]!=(u32)(i==11?transaction_nmi:transaction_trap))return 12;
 /* Exact examined cold PD latch baseline, maintained by each prior cleanup. */
 B(0xfffff746U)=0x3f;B(0xfffff740U)=1;
 for(i=0;i<4096;i++)__asm__ volatile("nop");
 return B(F)==128 && (B(0xfffff784U)&1) && W(0xfffff740U)==0x100U ? 0:13;
}
static u32 disable(void) {
 u32 low;B(F+4)=0;B(0xfffff746U)=0x3e;
 low=!(B(0xfffff746U)&1);
 B(0xfffff740U)=0;
 return low && protected() && W(0xfffff740U)==0 && !(B(0xfffff784U)&1)?0:14;
}
static u32 operation(u32 op,u32 index,u32 expected_crc) {
 u32 r,i,error;
 if(!S->initialized || S->tainted || (op==ERASE && S->erased) ||
    (op==PROGRAM && (!S->erased || index>=1024 || index!=S->next_unit)))return 15;
 if(op==PROGRAM && crc32(BUFFER,128)!=expected_crc)return 16;
 S->tainted=1; /* Before FWE or any chip operation: ambiguous completion poisons this generation. */
 __asm__ volatile("ldc %0,vbr"::"r"(VECTORS):"memory");
 if(op==ERASE) { BUSY_CALLS=0;BUSY_SERVICES=0; }
 error=enable();
 if(error) { disable();return error; }
 if(S->faults || S->service_error) { disable();return 17; }
 S->busy=1;B(F+4)=0x5a;
 if(op==ERASE)r=((u32 (*)(u32))0xffff2010U)(14);
 else r=((u32 (*)(u32,u32))0xffff1010U)((u32)BUFFER,0xc0000U+unit_offset(index));
 /* Only after the silicon routine returns may protection/FWE change. */
 S->fpfr=r;B(F+4)=0;S->busy=0;error=disable();
 if(r || error || S->faults || S->service_error)return error?error:17;
 if(op==ERASE) {
  for(i=0;i<0x20000U;i++) { if(B(0xc0000U+i)!=255)return 18;if(!(i&127))transaction_service(); }
  if(!BUSY_CALLS || !BUSY_SERVICES)return 25;
  S->erased=1;S->generation++;S->next_unit=0;
 } else {
  for(i=0;i<128;i++)if(B(0xc0000U+unit_offset(index)+i)!=BUFFER[i])return 19;
  S->next_unit++;
 }
 if(S->service_error)return 20;
 S->tainted=0;return 0;
}
#endif
void transaction_main(void) {
 u8 command[16];u32 i,op,seq,cookie,index,wanted,status,data_crc=0;const volatile u8 *data=0;u32 length=0;
 for(i=0;i<16;i++)command[i]=COMMAND[i];
 op=command[0];seq=get16(command+2);cookie=get32(command+4);index=get16(command+8);wanted=get32(command+12);
 S->fpfr=0;
 status=preflight();
 if(!status && (command[1] || command[10] || command[11] || !seq || !cookie))status=21;
 if(!status && op==0) {
  if(S->magic || seq!=1 || index || wanted)status=22;
  else { S->cookie=cookie;S->sequence=0;S->next_unit=0;S->generation=0;S->erased=0;S->tainted=0;
   S->initialized=0;S->busy=0;S->faults=0;
#if ENABLE_FLASH
   BUSY_CALLS=0;BUSY_SERVICES=0;
#endif
   status=initialize();if(!status)S->magic=MAGIC; }
 } else if(!status && (S->magic!=MAGIC || S->cookie!=cookie || seq!=S->sequence+1 || S->sequence==65535))status=23;
 if(!status && op!=0) {
  if(op==READ && index<2048 && !wanted) { data=(const volatile u8 *)(index*512U);length=512; }
  else if(op==STAGE && index<1024) {
   data=BUFFER;length=128;if(crc32(data,length)!=wanted) {status=16;length=0;}
  } else if(op==END && !index && !wanted) { if(!cleanup())status=11;else S->magic=0; }
#if ENABLE_FLASH
  else if(op==0x13U && index<1024 && !wanted) { data=(const volatile u8 *)(0xc0000U+unit_offset(index));length=128; }
  else if(op==ERASE && index==14 && !wanted)status=operation(op,0,0);
  else if(op==PROGRAM && index<1024)status=operation(op,index,wanted);
#endif
  else status=24;
 }
 if(!status && S->service_error)status=20;
 if(!status)S->sequence=seq;
 else if(S->magic==MAGIC)S->tainted=1;
 if(length && !status) { data_crc=crc32(data,length);stream(0xd0,data,length); }
 if(S->service_error || S->faults) {status=20;S->tainted=1;}
 put32(RESULT,MAGIC);put32(RESULT+4,cookie);put16(RESULT+8,seq);RESULT[10]=op;RESULT[11]=status;
 put32(RESULT+12,S->fpfr);put32(RESULT+16,data_crc);
 put32(RESULT+20,(S->initialized?1:0)|(S->erased?2:0)|(S->tainted?4:0)|(S->faults?8:0)|(S->service_error?16:0));
 #if ENABLE_FLASH
 if(S->initialized && BUSY_CALLS)RESULT[23]|=32;
 if(S->initialized && BUSY_SERVICES)RESULT[23]|=64;
#endif
 put32(RESULT+24,S->next_unit);put32(RESULT+28,(index<<16)|B(F));
 stream(0xf0,RESULT,32);
 if(S->service_error || S->faults) {status=20;S->tainted=1;}
 SCRATCH[0]='M';SCRATCH[1]='T';SCRATCH[2]=op;SCRATCH[3]=status;put16(SCRATCH+4,seq);send(0xc0,6,SCRATCH);
}

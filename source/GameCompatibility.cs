using System;
using System.IO;
namespace PBZL3Installer {
    // These four code spans identify the native sprint layout used by the adapter.
    static class GameCompatibility {
        sealed class Span {
            internal long Offset; internal int Length; internal string Sha256;
            internal Span(long offset, int length, string sha256) {Offset=offset;Length=length;Sha256=sha256;}
        }
        sealed class Profile {
            internal string Build; internal long Size; internal Span[] Spans;
            internal Profile(string build,long size,Span[] spans) {Build=build;Size=size;Spans=spans;}
        }
        static readonly Profile[] Profiles = {
            new Profile("26266538",511072344,new[] {
                new Span(98340640,355,"255908D5AC5D80A9252AD79D49D84E3173914A466D560C176FC9824F36CF03EB"),
                new Span(98341008,42,"1B39D9D0ACDE3F5A103EBB9F9237A1A448D4E69AEC6A67497E95D42CC5458D28"),
                new Span(98338688,100,"9E1927BD0DEA4E63E367B7A1C89E9F07385E291E5E8500D3C40A57F1E4C2A1DE"),
                new Span(98340528,105,"A0AC7EDC1F39BCD5130267AB0418609A394DC357AE5E3EFDD5B822F6589B849B"),
            }),
            new Profile("26367565",498913272,new[] {
                new Span(98639376,355,"07A27209277292492608134F49F073F70A056883CBBC13BE58C5D5F27C91827D"),
                new Span(98639744,42,"1B39D9D0ACDE3F5A103EBB9F9237A1A448D4E69AEC6A67497E95D42CC5458D28"),
                new Span(98637424,100,"8DCEFC1DC3AF1A95A03D2BCCD610F92A36DF9181691B4195158D1F6958081636"),
                new Span(98639264,105,"0BB54A1EB714268699D4E91E723D698BAED3AFB53ED5F1CC65344E2D3DDD4D6C"),
            }),
        };
        internal static string Verify(string filename) {
            using(var file=new FileStream(filename,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                foreach(var profile in Profiles) {
                    if(file.Length!=profile.Size)continue;
                    foreach(var span in profile.Spans) {
                        file.Seek(span.Offset,SeekOrigin.Begin);
                        byte[] bytes=new byte[span.Length];int read=0,n;
                        while(read<bytes.Length && (n=file.Read(bytes,read,bytes.Length-read))>0)read+=n;
                        if(read!=bytes.Length || Core.Hash(bytes)!=span.Sha256)
                            throw new IOException("游戏原生冲刺代码不匹配，已停止安装。请使用受支持的原版游戏。");
                    }
                    return profile.Build;
                }
            }
            throw new IOException("游戏版本不匹配。支持 Steam build 26266538 / 26367565；已停止安装。");
        }
    }
}

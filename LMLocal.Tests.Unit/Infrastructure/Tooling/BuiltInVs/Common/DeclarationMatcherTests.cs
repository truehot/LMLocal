using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class DeclarationMatcherTests
    {
        private static void AssertKind(string extension, string line, SearchMatchKind expected)
        {
            var actual = DeclarationMatcher.Classify(extension, line);
            Assert.That(actual, Is.EqualTo(expected), $"ext='{extension}' line='{line}'");
        }

        // --- C# ---

        [TestCase("class PaymentService")]
        [TestCase("public partial class Foo : IFoo")]
        [TestCase("internal sealed class Bar")]
        [TestCase("readonly record struct Point")]
        [TestCase("public enum Color")]
        [TestCase("public interface IRepo")]
        [TestCase("public delegate void Handler(int x)")]
        public void CSharp_Types(string line)
        {
            AssertKind(".cs", line, SearchMatchKind.Type);
        }

        [TestCase("public void Run()")]
        [TestCase("private static async Task<int> GetAsync()")]
        [TestCase("int Add(int a, int b)")]
        [TestCase("public PaymentService(IService s)")]
        [TestCase("public static void Main(string[] args)")]
        [TestCase("void Execute()")]
        [TestCase("void Foo<T>(T x)")]
        [TestCase("public PaymentService<T>(T x)")]
        [TestCase("Task<IEnumerable<T>> GetAsync<T>()")]
        public void CSharp_Functions(string line)
        {
            AssertKind(".cs", line, SearchMatchKind.Function);
        }

        [TestCase("public string Name { get; set; }")]
        [TestCase("public int Count { get; }")]
        [TestCase("public bool IsReady => true;")]
        public void CSharp_Properties(string line)
        {
            AssertKind(".cs", line, SearchMatchKind.Property);
        }

        [TestCase("private int _count;")]
        [TestCase("public const int Max = 10;")]
        [TestCase("private readonly string _name = null;")]
        [TestCase("internal static int StaticCounter;")]
        public void CSharp_Fields(string line)
        {
            AssertKind(".cs", line, SearchMatchKind.Field);
        }

        [TestCase("// public class Foo")]
        [TestCase("/// <summary>class Foo</summary>")]
        [TestCase("var x = new List<int>();")]
        [TestCase("return Foo();")]
        [TestCase("new PaymentService();")]
        [TestCase("if (x > 0) {")]
        [TestCase("foreach (var item in items)")]
        [TestCase("using System;")]
        [TestCase("return x;")]
        [TestCase("throw ex;")]
        [TestCase("goto label;")]
        [TestCase("await x;")]
        public void CSharp_NotDeclarations(string line)
        {
            AssertKind(".cs", line, SearchMatchKind.Other);
        }

        // --- C++ ---

        [TestCase("class Foo")]
        [TestCase("struct Point {")]
        [TestCase("template <typename T> class Box")]
        [TestCase("namespace MyNs {")]
        [TestCase("enum Color { RED, GREEN }")]
        public void Cpp_Types(string line)
        {
            AssertKind(".cpp", line, SearchMatchKind.Type);
        }

        [TestCase("int Add(int a, int b)")]
        [TestCase("void Foo::Bar()")]
        [TestCase("Foo::Foo(int x)")]
        [TestCase("static int Count()")]
        [TestCase("void Run() const")]
        [TestCase("template<class T> void Foo(T x)")]
        [TestCase("void Foo<T>(T x)")]
        public void Cpp_Functions(string line)
        {
            AssertKind(".cpp", line, SearchMatchKind.Function);
        }

        [TestCase("int x = 5;")]
        [TestCase("static int counter;")]
        [TestCase("std::string name;")]
        public void Cpp_Fields(string line)
        {
            AssertKind(".cpp", line, SearchMatchKind.Field);
        }

        [TestCase("return foo();")]
        [TestCase("if (x) {")]
        [TestCase("#include <vector>")]
        [TestCase("new Foo();")]
        [TestCase("return x;")]
        [TestCase("throw ex;")]
        [TestCase("goto label;")]
        [TestCase("delete ptr;")]
        public void Cpp_NotDeclarations(string line)
        {
            AssertKind(".cpp", line, SearchMatchKind.Other);
        }

        // --- JS ---

        [TestCase("class PaymentService {")]
        [TestCase("export default class Foo")]
        [TestCase("export class Bar")]
        public void Js_Types(string line)
        {
            AssertKind(".js", line, SearchMatchKind.Type);
        }

        [TestCase("function foo() {")]
        [TestCase("async function load() {")]
        [TestCase("export function helper() {")]
        [TestCase("function* gen() {")]
        [TestCase("const foo = () => {")]
        [TestCase("const handler = async (e) => {")]
        public void Js_Functions(string line)
        {
            AssertKind(".js", line, SearchMatchKind.Function);
        }

        [TestCase("this.name = value;")]
        public void Js_Field(string line)
        {
            AssertKind(".js", line, SearchMatchKind.Field);
        }

        [TestCase("const x = 5;")]
        [TestCase("let y = [1,2,3];")]
        [TestCase("// class Foo")]
        public void Js_NotDeclarations(string line)
        {
            AssertKind(".js", line, SearchMatchKind.Other);
        }

        // --- TS ---

        [TestCase("interface IRepo")]
        [TestCase("type UserId = string")]
        [TestCase("enum Color")]
        [TestCase("abstract class Base")]
        [TestCase("namespace MyNs")]
        public void Ts_Types(string line)
        {
            AssertKind(".ts", line, SearchMatchKind.Type);
        }

        [TestCase("name: string;")]
        [TestCase("private readonly id: number;")]
        public void Ts_Properties(string line)
        {
            AssertKind(".ts", line, SearchMatchKind.Property);
        }

        [TestCase("function foo(): void {")]
        [TestCase("const bar = (): number => {")]
        [TestCase("function foo<T>(x: T): void {")]
        public void Ts_Functions(string line)
        {
            AssertKind(".ts", line, SearchMatchKind.Function);
        }

        // --- VB ---

        [TestCase("Public Class Foo")]
        [TestCase("Private Module M")]
        [TestCase("Public Structure Point")]
        [TestCase("Public Interface IThing")]
        public void Vb_Types(string line)
        {
            AssertKind(".vb", line, SearchMatchKind.Type);
        }

        [TestCase("Public Function Foo() As String")]
        [TestCase("Private Sub Init()")]
        [TestCase("Public Sub DoWork")]
        public void Vb_Functions(string line)
        {
            AssertKind(".vb", line, SearchMatchKind.Function);
        }

        [TestCase("Public Property Name As String")]
        [TestCase("Public Event DataReceived As EventHandler")]
        public void Vb_Properties(string line)
        {
            AssertKind(".vb", line, SearchMatchKind.Property);
        }

        [TestCase("Private _count As Integer")]
        [TestCase("Public Const Max As Integer = 10")]
        [TestCase("Dim x As Integer")]
        public void Vb_Fields(string line)
        {
            AssertKind(".vb", line, SearchMatchKind.Field);
        }

        [TestCase("' Public Class Foo")]
        public void Vb_NotDeclarations(string line)
        {
            AssertKind(".vb", line, SearchMatchKind.Other);
        }

        // --- Python / Go / Rust ---

        [TestCase(".py", "class PaymentService:", SearchMatchKind.Type)]
        [TestCase(".py", "def connect(self):", SearchMatchKind.Function)]
        [TestCase(".py", "async def load():", SearchMatchKind.Function)]
        [TestCase(".go", "type Server struct {", SearchMatchKind.Type)]
        [TestCase(".go", "func (s *Server) Start() {", SearchMatchKind.Function)]
        [TestCase(".go", "func main() {", SearchMatchKind.Function)]
        [TestCase(".go", "func Foo[T any](x T) {", SearchMatchKind.Function)]
        [TestCase(".rs", "struct Point {", SearchMatchKind.Type)]
        [TestCase(".rs", "impl Point {", SearchMatchKind.Type)]
        [TestCase(".rs", "fn compute(x: i32) {", SearchMatchKind.Function)]
        [TestCase(".rs", "fn foo<T>(x: T) {", SearchMatchKind.Function)]
        public void OtherLanguages(string ext, string line, SearchMatchKind expected)
        {
            AssertKind(ext, line, expected);
        }

        // --- generic fallback ---

        [TestCase("class Foo")]
        [TestCase("type Foo =")]
        [TestCase("struct Foo {")]
        public void GenericFallback_Types(string line)
        {
            AssertKind(".kt", line, SearchMatchKind.Type);
        }

        [TestCase("fun foo() {")]
        [TestCase("function foo() {")]
        public void GenericFallback_Functions(string line)
        {
            AssertKind(".kt", line, SearchMatchKind.Function);
        }

        [TestCase(null, "class Foo")]
        [TestCase("", "public class Foo")]
        public void UnknownExtension_FallsBackToGeneric(string extension, string line)
        {
            AssertKind(extension, line, SearchMatchKind.Type);
        }
    }
}

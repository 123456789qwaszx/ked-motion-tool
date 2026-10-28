# Ked.Presentation.Core — 복사 반입본 (정본은 ked-stage-runtime)

`ked-stage-runtime` 저장소 `Assets/Ked.Presentation/Core/`의 복사본이다.
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: 무대 런타임 → 이 사본.**
패키지화는 하지 않는다(소유자 결정). 아래 절차가 반복 가능한 대체물이다.

| 항목 | 값 |
|---|---|
| 원본 | `ked-stage-runtime` `dev` @ `640ac60` (motions/2, 빈 모션 허용) |
| 반입일 | 2026-10-28 |
| 범위 | `Ease/`·`Motion/`·`Primitives/`·`Reduce/`·`State/`·`Tokens/`·`Transforms/`·`Tuning/` 소스와 `Documentation~/` |
| 제외 | `Tests/`·`*.meta`·`*.asmdef` |
| 테스트 | 순수 테스트(유니티·런타임·DOTween 참조 없는 것)만 `tests/Ked.Presentation.Core.Tests/EditMode/`로 |
| 이쪽 살림 | `Ked.Presentation.Core.csproj`·`vendoring.md` (동기화 때 유지) |

## 동기화 절차

1. **감사** — `diff -r`로 세 갈래를 가른다: ⓐ 저쪽에만 있는 파일(가져온다) ⓑ 내용이 다른
   파일(저쪽 본으로 교체) ⓒ 이쪽에만 있는 코드 멤버 — 그건 과거 로컬 수정이다. 목록으로
   남기고 저쪽 반영을 소유자에게 물은 뒤 처리한다.
2. **이식** — 저쪽 트리에서 `Tests/`·`*.meta`·`*.asmdef` 제외 전부 복사. 테스트는
   `Tests/EditMode/*.cs` 중 순수한 것만 `tests/Ked.Presentation.Core.Tests/EditMode/`로.
   기준: `UnityEngine`·`UnityEditor`·`DG.`·`Ked.Presentation.Runtime/Editor`·
   `Ked.CommandExecutor`·`Newtonsoft`·`LogAssert`를 참조하면 제외.
3. **보수** — 소비자(`Mt.Authoring`·`Mt.App`)의 컴파일·의미 적응은 **소비자 쪽에서** 고친다.
   이 사본의 파일을 고쳐 맞추지 않는다 — 사본을 고치는 순간 다음 동기화가 그 수정을
   조용히 지운다.
4. **고정** — 전체 빌드 + 전체 테스트. 이 표의 원본 커밋·반입일을 갱신한다.

## 검증

```
diff -r --exclude=Tests --exclude='*.meta' --exclude='*.asmdef' \
        --exclude=Ked.Presentation.Core.csproj --exclude=vendoring.md \
        <ked-stage-runtime>/Assets/Ked.Presentation/Core src/Ked.Presentation.Core
```

차이 0줄이 동기화 완료다.

## 유니티 호환 안전핀

csproj가 원본 asmdef의 `noEngineReferences: true`와 유니티 C# 수준(LangVersion 9,
netstandard2.1)을 컴파일러 제약으로 유지한다.

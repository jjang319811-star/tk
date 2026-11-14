using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TK.EditorTools
{
    /// <summary>
    /// 픽셀라이즈 컴포지트 씬: Demo 씬을 복사한 뒤, 게임 월드 전체를 저해상도(480x270) 한 장으로 렌더하고
    /// 정수배로 확대하는 URP Pixel Perfect Camera 내장 'Upscale Render Texture' 모드를 켬.
    ///  → 캐릭터·맵·장식·이펙트·조명·Bloom·회전하는 무기·장판까지 모두 같은 픽셀 격자 하나로 통합
    ///  → 화면 UI(HUD·아이콘·텍스트·데미지 숫자)는 Screen Space Overlay 라 영향 없음 (선명한 UI 유지)
    /// 원본 Demo 씬은 그대로 두고, Build All 마다 이 씬도 다시 생성해 항상 같은 내용을 유지.
    /// 메뉴: Tools/TK/Build Pixel Composite Scene
    /// </summary>
    public static partial class TKGameBuilder
    {
        public const string CompositeScenePath = "Assets/Scenes/Demo_PixelComposite.unity";

        [MenuItem("Tools/TK/Build Pixel Composite Scene")]
        public static void CompositeMenu() => Debug.Log(BuildPixelCompositeScene());

        public static string BuildPixelCompositeScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Play 모드에서는 만들 수 없습니다.";

            // 원본 저장 후 복사 (열려 있는 원본의 변경 사항이 복사본에 빠지지 않도록)
            var source = SceneManager.GetSceneByPath(ScenePath);
            if (source.IsValid() && source.isLoaded && source.isDirty) EditorSceneManager.SaveScene(source);
            var composite = SceneManager.GetSceneByPath(CompositeScenePath);
            if (composite.IsValid() && composite.isLoaded)
            {
                if (SceneManager.sceneCount == 1) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                else EditorSceneManager.CloseScene(composite, true);
            }
            AssetDatabase.DeleteAsset(CompositeScenePath);
            if (!AssetDatabase.CopyAsset(ScenePath, CompositeScenePath))
                return "Demo 씬 복사 실패";

            var scene = EditorSceneManager.OpenScene(CompositeScenePath, OpenSceneMode.Additive);
            var ppc = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PixelPerfectCamera>(true)).FirstOrDefault();
            if (ppc == null) { EditorSceneManager.CloseScene(scene, true); return "Pixel Perfect Camera 없음"; }

            ppc.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture; // 월드 전체를 한 장의 저해상도 픽셀 화면으로
            ppc.cropFrame = PixelPerfectCamera.CropFrame.None;
            EditorUtility.SetDirty(ppc);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.CloseScene(scene, true);

            // 빌드 목록에 추가 (원본 Demo 가 첫 번째 = 기본 실행 씬 유지)
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == CompositeScenePath))
            {
                list.Add(new EditorBuildSettingsScene(CompositeScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            return $"Pixel composite scene: {CompositeScenePath} (PPC Upscale Render Texture {ppc.refResolutionX}x{ppc.refResolutionY})";
        }
    }
}

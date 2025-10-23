// Assets/Editor/BoneConstraintMapperWindow.cs
// Unity 2020+ / 2022+ / 6000+ (Unity 6) 호환
// 기능: 두 GameObject(A=Source, B=Target) 본 매칭 후, Target 본들에 RotationConstraint 자동 설정/제거
// - Humanoid면 Animator.GetBoneTransform 기반 자동매핑
// - Generic이면 임포터를 잠시 Humanoid로 바꿔 자동매핑 추출(가능할 때만; 에셋 경로 필요)
// - UI: A/B 드래그, Body/Head/Hand(양손) 그룹으로 매핑 리스트 노출, 수동 드래그 수정 가능
// - Apply: RotationConstraint 생성 + Source 지정 + 활성화
// - Remove: 생성된 RotationConstraint 제거

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;

public class BoneConstraintMapperWindow : EditorWindow
{
    [Serializable]
    public class BoneLink
    {
        public string label;
        public HumanBodyBones humanBone;
        public Transform src;
        public Transform dst;

        // --- 새 필드 ---
        public bool isOptional;   // ★ 추가: 선택 본 표시
        public bool enabled = true; // ★ 추가: 적용 대상 체크박스
    }

    private const float COL_LABEL_W = 150f; // 본 라벨 폭
    private const float COL_FIELD_W = 240f; // ObjectField 폭 (A/B 동일)
    private const float COL_GAP_AB = 14f;  // A-B 사이 간격
    private const float COL_RC_W = 70f;

    private GameObject _sourceRoot; // A
    private GameObject _targetRoot; // B

    // 그룹화: Body / Head / Hands(L/R)
    private readonly List<BoneLink> _bodyLinks = new List<BoneLink>();
    private readonly List<BoneLink> _headLinks = new List<BoneLink>();
    private readonly List<BoneLink> _handLinks = new List<BoneLink>();

    // 표시/토글
    private Vector2 _scroll;
    private bool _foldBody = true;
    private bool _foldHead = true;
    private bool _foldHand = true;

    [MenuItem("SEJUN/Bone Constraint Mapper")]
    private static void Open()
    {
        var win = GetWindow<BoneConstraintMapperWindow>("Bone Constraint Mapper");
        win.minSize = new Vector2(520, 420);
        win.InitializeDefault();
    }

    private void OnEnable()
    {
        InitializeDefault();
    }

    // ★ 변경: 기본 매핑에 '선택 본'을 포함하고, isOptional 세팅
    private void InitializeDefault()
    {
        _bodyLinks.Clear();
        _headLinks.Clear();
        _handLinks.Clear();

        // Body: 필수 + 선택(UpperChest, Shoulders, Toes 등)
        _bodyLinks.AddRange(new[]
        {
        // 필수 본
        new BoneLink{ label = "Hips",          humanBone = HumanBodyBones.Hips,          isOptional = false },
        new BoneLink{ label = "Spine",         humanBone = HumanBodyBones.Spine,         isOptional = false },
        new BoneLink{ label = "Chest",         humanBone = HumanBodyBones.Chest,         isOptional = false },

        // 선택 본
        new BoneLink{ label = "UpperChest",    humanBone = HumanBodyBones.UpperChest,    isOptional = true  }, // ★ 추가
        new BoneLink{ label = "LeftShoulder",  humanBone = HumanBodyBones.LeftShoulder,  isOptional = true  }, // ★ 추가
        new BoneLink{ label = "RightShoulder", humanBone = HumanBodyBones.RightShoulder, isOptional = true  }, // ★ 추가

        // 팔(대개 필수)
        new BoneLink{ label = "LeftUpperArm",  humanBone = HumanBodyBones.LeftUpperArm,  isOptional = false },
        new BoneLink{ label = "RightUpperArm", humanBone = HumanBodyBones.RightUpperArm, isOptional = false },
        new BoneLink{ label = "LeftLowerArm",  humanBone = HumanBodyBones.LeftLowerArm,  isOptional = false },
        new BoneLink{ label = "RightLowerArm", humanBone = HumanBodyBones.RightLowerArm, isOptional = false },

        // 다리(대개 필수, Toes는 선택)
        new BoneLink{ label = "LeftUpperLeg",  humanBone = HumanBodyBones.LeftUpperLeg,  isOptional = false }, // ★ 추가
        new BoneLink{ label = "RightUpperLeg", humanBone = HumanBodyBones.RightUpperLeg, isOptional = false }, // ★ 추가
        new BoneLink{ label = "LeftLowerLeg",  humanBone = HumanBodyBones.LeftLowerLeg,  isOptional = false }, // ★ 추가
        new BoneLink{ label = "RightLowerLeg", humanBone = HumanBodyBones.RightLowerLeg, isOptional = false }, // ★ 추가
        new BoneLink{ label = "LeftFoot",      humanBone = HumanBodyBones.LeftFoot,      isOptional = false }, // ★ 추가
        new BoneLink{ label = "RightFoot",     humanBone = HumanBodyBones.RightFoot,     isOptional = false }, // ★ 추가
        new BoneLink{ label = "LeftToes",      humanBone = HumanBodyBones.LeftToes,      isOptional = true  }, // ★ 추가
        new BoneLink{ label = "RightToes",     humanBone = HumanBodyBones.RightToes,     isOptional = true  }, // ★ 추가
    });

        // Head: Neck/Head 필수 + Jaw/Eyes 선택
        _headLinks.AddRange(new[]
        {
        new BoneLink{ label = "Neck",          humanBone = HumanBodyBones.Neck,          isOptional = false },
        new BoneLink{ label = "Head",          humanBone = HumanBodyBones.Head,          isOptional = false },
        new BoneLink{ label = "Jaw",           humanBone = HumanBodyBones.Jaw,           isOptional = true  }, // ★ 추가
        new BoneLink{ label = "LeftEye",       humanBone = HumanBodyBones.LeftEye,       isOptional = true  }, // ★ 추가
        new BoneLink{ label = "RightEye",      humanBone = HumanBodyBones.RightEye,      isOptional = true  }, // ★ 추가
    });

        // Hand: 손목 필수 + 손가락(대표 루트) 선택
        _handLinks.AddRange(new[]
        {
        new BoneLink{ label = "LeftHand",      humanBone = HumanBodyBones.LeftHand,      isOptional = false },
        new BoneLink{ label = "RightHand",     humanBone = HumanBodyBones.RightHand,     isOptional = false },

        // 손가락 루트(세부 관절은 원하면 추가 확장)
        new BoneLink{ label = "LeftThumbProximal",  humanBone = HumanBodyBones.LeftThumbProximal,  isOptional = true }, // ★ 추가
        new BoneLink{ label = "LeftIndexProximal",  humanBone = HumanBodyBones.LeftIndexProximal,  isOptional = true }, // ★ 추가
        new BoneLink{ label = "LeftMiddleProximal", humanBone = HumanBodyBones.LeftMiddleProximal, isOptional = true }, // ★ 추가
        new BoneLink{ label = "LeftRingProximal",   humanBone = HumanBodyBones.LeftRingProximal,   isOptional = true }, // ★ 추가
        new BoneLink{ label = "LeftLittleProximal", humanBone = HumanBodyBones.LeftLittleProximal, isOptional = true }, // ★ 추가

        new BoneLink{ label = "RightThumbProximal",  humanBone = HumanBodyBones.RightThumbProximal,  isOptional = true }, // ★ 추가
        new BoneLink{ label = "RightIndexProximal",  humanBone = HumanBodyBones.RightIndexProximal,  isOptional = true }, // ★ 추가
        new BoneLink{ label = "RightMiddleProximal", humanBone = HumanBodyBones.RightMiddleProximal, isOptional = true }, // ★ 추가
        new BoneLink{ label = "RightRingProximal",   humanBone = HumanBodyBones.RightRingProximal,   isOptional = true }, // ★ 추가
        new BoneLink{ label = "RightLittleProximal", humanBone = HumanBodyBones.RightLittleProximal, isOptional = true }, // ★ 추가
    });
    }


    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Source / Target", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            _sourceRoot = (GameObject)EditorGUILayout.ObjectField(new GUIContent("A (Source Root)"), _sourceRoot, typeof(GameObject), true);
            _targetRoot = (GameObject)EditorGUILayout.ObjectField(new GUIContent("B (Target Root)"), _targetRoot, typeof(GameObject), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _sourceRoot && _targetRoot;
                if (GUILayout.Button("자동찾기 (A→B 매핑 생성/갱신)"))
                {
                    AutoAssignAll();
                }
                GUI.enabled = true;
            }
        }

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawGroup("Body", ref _foldBody, _bodyLinks);
        DrawGroup("Head", ref _foldHead, _headLinks);
        DrawGroup("Hand", ref _foldHand, _handLinks);

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = _targetRoot;
            if (GUILayout.Button("Apply (RotationConstraint 부착/갱신)", GUILayout.Height(28)))
            {
                ApplyConstraints();
            }
            if (GUILayout.Button("Remove (RotationConstraint 제거)", GUILayout.Height(28)))
            {
                RemoveConstraints();
            }
            GUI.enabled = true;
        }
    }

    private void DrawGroup(string title, ref bool fold, List<BoneLink> list)
    {
        fold = EditorGUILayout.Foldout(fold, title, true, EditorStyles.foldoutHeader);
        if (!fold)
            return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            // ── 헤더 라인 ──────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(18); // 체크박스 자리 맞춤
                EditorGUILayout.LabelField("본", GUILayout.Width(COL_LABEL_W));
                EditorGUILayout.LabelField("A (Source)", GUILayout.Width(COL_FIELD_W));
                GUILayout.Space(COL_GAP_AB);
                EditorGUILayout.LabelField("B (Target)", GUILayout.Width(COL_FIELD_W));
                var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                EditorGUILayout.LabelField("RC", style, GUILayout.Width(COL_RC_W));
            }

            EditorGUILayout.Space(3);

            // ── 본 매핑 라인들 ─────────────────────────────────────────
            foreach (var link in list)
            {
                var rect = EditorGUILayout.BeginHorizontal(); // ★ 행의 전체 rect 확보

                // ★ 체크된 경우, 배경 먼저 그리기 (레이아웃에는 영향 X)
                if (Event.current.type == EventType.Repaint && link.enabled)
                {
                    var bg = EditorGUIUtility.isProSkin
                        ? new Color(0.25f, 0.55f, 0.35f, 0.18f)
                        : new Color(0.45f, 0.75f, 0.55f, 0.18f);
                    EditorGUI.DrawRect(rect, bg);
                }

                // 기존 내용 동일
                link.enabled = EditorGUILayout.ToggleLeft(GUIContent.none, link.enabled, GUILayout.Width(18));

                var label = link.label + (link.isOptional ? "  (Optional)" : "");
                EditorGUILayout.LabelField(label, GUILayout.Width(COL_LABEL_W));

                link.src = (Transform)EditorGUILayout.ObjectField(GUIContent.none, link.src, typeof(Transform), true, GUILayout.Width(COL_FIELD_W));
                GUILayout.Space(COL_GAP_AB);
                link.dst = (Transform)EditorGUILayout.ObjectField(GUIContent.none, link.dst, typeof(Transform), true, GUILayout.Width(COL_FIELD_W));

                DrawRCStatus(link.dst, COL_RC_W);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);

            // 일괄 토글 버튼
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("모두 체크", GUILayout.Width(100)))
                    foreach (var l in list)
                        l.enabled = true;

                if (GUILayout.Button("필수만 체크", GUILayout.Width(110)))   // ★ 추가
                    foreach (var l in list)
                        l.enabled = !l.isOptional;        // ★ 필수만 true

                if (GUILayout.Button("모두 해제", GUILayout.Width(100)))
                    foreach (var l in list)
                        l.enabled = false;
            }
        }
    }

    private void DrawRCStatus(Transform dst, float width)
    {
        string text;
        Color col;

        if (dst == null)
        {
            text = "-";
            col = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
        }
        else
        {
            var rc = dst.GetComponent<UnityEngine.Animations.RotationConstraint>();
            if (rc == null)
            {
                text = "없음";
                col = EditorGUIUtility.isProSkin ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.3f, 0.3f, 0.3f);
            }
            else
            {
                // On/Off + 소스 수
                var on = rc.constraintActive;
                text = on ? $"On ({rc.sourceCount})" : $"Off ({rc.sourceCount})";
                col = on ? new Color(0.3f, 0.8f, 0.4f) : new Color(1.0f, 0.75f, 0.25f);
            }
        }

        // 색상 적용 후 표시
        var prev = GUI.contentColor;
        GUI.contentColor = col;
        var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        EditorGUILayout.LabelField(text, style, GUILayout.Width(width));
        GUI.contentColor = prev;
    }


    // ===========================
    // 자동 매핑
    // ===========================
    // ★ 변경: AutoAssignAll — 양쪽 모두 'Humanoid 기반 맵'을 강제 확보.
    //         하나라도 실패하면 중단(이름기반 보조 매칭 제거).
    private void AutoAssignAll()
    {
        if (_sourceRoot == null || _targetRoot == null)
        {
            ShowNotify("A/B를 먼저 드래그해줘.");
            return;
        }

        try
        {
            var srcMap = GetOrExtractHumanoidMap(_sourceRoot); // ★ 항상 Humanoid 의존
            var dstMap = GetOrExtractHumanoidMap(_targetRoot); // ★ 항상 Humanoid 의존

            if (srcMap == null || dstMap == null)
            {
                ShowNotify("Humanoid 자동 매핑을 확보하지 못했어. 콘솔 로그를 확인해줘.");
                return;
            }

            AutoFillByHumanMap(_bodyLinks, srcMap, dstMap);
            AutoFillByHumanMap(_headLinks, srcMap, dstMap);
            AutoFillByHumanMap(_handLinks, srcMap, dstMap);

            ShowNotify("양쪽 모두 Humanoid 기반으로 자동할당 완료.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowNotify("자동할당 중 오류 발생. 콘솔을 확인해줘.");
        }
    }


    private void AutoFillByHumanMap(List<BoneLink> links, Dictionary<HumanBodyBones, Transform> src, Dictionary<HumanBodyBones, Transform> dst)
    {
        foreach (var link in links)
        {
            if (src != null && src.TryGetValue(link.humanBone, out var s))
                link.src = s;
            if (dst != null && dst.TryGetValue(link.humanBone, out var d))
                link.dst = d;
        }
    }

    private Transform FindByNamePathInScene(Transform sceneRoot, Transform assetBone)
    {
        // 에셋 본의 경로를 구성해 씬 쪽에서 같은 경로명을 찾는다.
        var path = BuildPath(assetBone);
        return FindByPath(sceneRoot, path);
    }

    private string BuildPath(Transform t)
    {
        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }

    private Transform FindByPath(Transform root, string fullPath)
    {
        // fullPath의 최상위가 root.name 과 다르면 루트명 불일치 → 이름만 하위부터 탐색
        var parts = fullPath.Split('/');
        int start = 0;
        if (!string.Equals(parts[0], root.name, StringComparison.Ordinal))
        {
            // 루트명 다르면 하위부터 부분탐색
            return FindDeepByLeafPath(root, parts);
        }
        // 루트명 일치
        var current = root;
        for (int i = 1; i < parts.Length; i++)
        {
            var child = current.Find(parts[i]);
            if (child == null)
                return null;
            current = child;
        }
        return current;
    }

    private Transform FindDeepByLeafPath(Transform root, string[] parts)
    {
        // 마지막 이름을 기준으로 모든 하위에서 후보를 찾고, 위로 거슬러 경로 유사 여부 확인
        var leaf = parts[parts.Length - 1];
        var candidates = new List<Transform>();
        CollectByName(root, leaf, candidates);
        foreach (var c in candidates)
        {
            // 상위로 경로를 만들어 끝부분이 유사하면 채택
            var path = BuildPath(c);
            if (path.EndsWith("/" + leaf, StringComparison.Ordinal))
                return c;
        }
        return null;
    }

    private void CollectByName(Transform t, string name, List<Transform> list)
    {
        if (t.name == name)
            list.Add(t);
        for (int i = 0; i < t.childCount; i++)
            CollectByName(t.GetChild(i), name, list);
    }

    // ===========================
    // Constraint 적용/제거
    // ===========================
    private void ApplyConstraints()
    {
        if (_targetRoot == null)
        {
            ShowNotify("Target이 없습니다.");
            return;
        }

        int applied = 0;
        applied += ApplyList(_bodyLinks);
        applied += ApplyList(_headLinks);
        applied += ApplyList(_handLinks);

        ShowNotify(applied > 0 ? $"RotationConstraint {applied}개 적용/갱신 완료." : "적용할 매핑이 없습니다.");
    }

    // ★ 변경: enabled 체크 + src/dst 유효할 때만 적용
    private int ApplyList(List<BoneLink> list)
    {
        int count = 0;
        foreach (var link in list)
        {
            if (!link.enabled)
                continue;                 // ★ 추가: 체크된 항목만
            if (link.src == null || link.dst == null)    // 매핑 불완전 시 스킵
                continue;

            var dst = link.dst;
            Undo.RegisterFullObjectHierarchyUndo(dst.gameObject, "Apply RotationConstraint");

            var rc = dst.GetComponent<RotationConstraint>();
            if (rc == null)
                rc = Undo.AddComponent<RotationConstraint>(dst.gameObject);

            var sources = new List<ConstraintSource>
        {
            new ConstraintSource { sourceTransform = link.src, weight = 1f }
        };
            rc.SetSources(sources);
            rc.weight = 1f;
            rc.locked = false;
            rc.constraintActive = true;
            EditorUtility.SetDirty(rc);

            count++;
        }
        return count;
    }


    private void RemoveConstraints()
    {
        if (_targetRoot == null)
        {
            ShowNotify("Target이 없습니다.");
            return;
        }

        int removed = 0;
        removed += RemoveList(_bodyLinks);
        removed += RemoveList(_headLinks);
        removed += RemoveList(_handLinks);

        ShowNotify(removed > 0 ? $"RotationConstraint {removed}개 제거 완료." : "제거할 Constraint가 없습니다.");
    }

    // ★ 변경: enabled 체크를 존중해서 제거(원하면 모두 제거로 바꿔도 됨)
    private int RemoveList(List<BoneLink> list)
    {
        int count = 0;
        foreach (var link in list)
        {
            if (!link.enabled)
                continue; // ★ 추가: 체크된 항목만 제거
            if (link.dst == null)
                continue;

            var rc = link.dst.GetComponent<RotationConstraint>();
            if (rc != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(link.dst.gameObject, "Remove RotationConstraint");
                Undo.DestroyObjectImmediate(rc);
                count++;
            }
        }
        return count;
    }


    private void ShowNotify(string msg)
    {
        this.ShowNotification(new GUIContent(msg));
        Debug.Log($"[BoneConstraintMapper] {msg}");
    }

    // ★ 변경: 항상 Humanoid 기반 맵을 확보 (Humanoid면 즉시, Generic이면 임시 복제/전환 or Avatar.humanDescription 폴백)
    private Dictionary<HumanBodyBones, Transform> GetOrExtractHumanoidMap(GameObject root)
    {
        if (root == null)
            return null;

        var animator = root.GetComponentInChildren<Animator>();
        // 이미 Humanoid 아바타라면 바로 GetBoneTransform 사용
        if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
        {
            var map = new Dictionary<HumanBodyBones, Transform>();
            foreach (HumanBodyBones h in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (h == HumanBodyBones.LastBone)
                    continue;
                var t = animator.GetBoneTransform(h);
                if (t != null)
                    map[h] = t;
            }
            if (map.Count > 0)
                return map;
            // (희귀) GetBoneTransform가 비어있다면 아래 폴백으로 진행
        }

        // Generic 또는 Humanoid지만 본 조회 실패 → 원본 에셋에서 추출 시도
        return ExtractMapFromTemporaryClone(root);
    }


    // ★ 변경: Instantiate된 대상도 Animator.avatar 경로로 원본을 찾고,
    // FBX가 아니거나 ModelImporter가 없으면 Avatar.humanDescription에서 본 이름으로 "정확 매핑" 복구.
    private Dictionary<HumanBodyBones, Transform> ExtractMapFromTemporaryClone(GameObject sceneRoot)
    {
        try
        {
            var modelPath = ResolveModelAssetPath(sceneRoot); // ★ avatar 또는 prefab 원본 경로
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogWarning("[BoneConstraintMapper] 원본 에셋 경로를 찾을 수 없어 Humanoid 추출 불가");
                return null;
            }

            // Standalone .avatar(.asset) 인 경우: 임시 복제 대신 avatar의 humanDescription을 그대로 사용
            if (Path.GetExtension(modelPath).Equals(".asset", StringComparison.OrdinalIgnoreCase))
            {
                var animator = sceneRoot.GetComponentInChildren<Animator>();
                if (animator?.avatar != null && animator.avatar.isValid)
                {
                    // ★ Avatar의 공식 매핑(humanDescription.human)을 이용해 정확히 씬 본에 매핑
                    return BuildMapFromAvatarDescription(sceneRoot.transform, animator.avatar);
                }
                return null;
            }

            // FBX/모델 경로라면 임시 복제 → Humanoid 전환 → 자동매핑 추출
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                // 모델 임포터가 아니면 위와 같은 Avatar 폴백 시도
                var animator = sceneRoot.GetComponentInChildren<Animator>();
                if (animator?.avatar != null && animator.avatar.isValid)
                    return BuildMapFromAvatarDescription(sceneRoot.transform, animator.avatar);

                Debug.LogWarning("[BoneConstraintMapper] ModelImporter가 없어 임시 Humanoid 추출 불가");
                return null;
            }

            // 임시 폴더/경로
            var tmpFolder = "Assets/__TempHumanoidMaps";
            if (!AssetDatabase.IsValidFolder(tmpFolder))
                AssetDatabase.CreateFolder("Assets", "__TempHumanoidMaps");

            var srcName = Path.GetFileNameWithoutExtension(modelPath);
            var ext = Path.GetExtension(modelPath);
            var tmpPath = AssetDatabase.GenerateUniqueAssetPath($"{tmpFolder}/{srcName}__TMP{ext}");

            // 복제
            if (!AssetDatabase.CopyAsset(modelPath, tmpPath))
            {
                Debug.LogWarning($"[BoneConstraintMapper] 에셋 복제 실패: {modelPath} → {tmpPath}");
                return null;
            }

            // 임시 복제본을 Humanoid + Auto 로
            var tmpImporter = AssetImporter.GetAtPath(tmpPath) as ModelImporter;
            if (tmpImporter == null)
            {
                AssetDatabase.DeleteAsset(tmpPath);
                return null;
            }

            var origType = tmpImporter.animationType;
            var origAuto = tmpImporter.autoGenerateAvatarMappingIfUnspecified;
            try
            {
                tmpImporter.animationType = ModelImporterAnimationType.Human; // ★ 강제 Humanoid
                tmpImporter.autoGenerateAvatarMappingIfUnspecified = true;
                tmpImporter.SaveAndReimport();

                var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(tmpPath);
                if (modelRoot == null)
                {
                    AssetDatabase.DeleteAsset(tmpPath);
                    return null;
                }

                var anim = modelRoot.GetComponentInChildren<Animator>();
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
                {
                    AssetDatabase.DeleteAsset(tmpPath);
                    Debug.LogWarning("[BoneConstraintMapper] 임시 복제본에서 Humanoid Avatar 생성 실패");
                    return null;
                }

                // 임시 모델의 본 경로 → 씬의 동일 본 Transform 매칭
                var result = new Dictionary<HumanBodyBones, Transform>();
                foreach (HumanBodyBones h in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (h == HumanBodyBones.LastBone)
                        continue;
                    var tmpBone = anim.GetBoneTransform(h);
                    if (tmpBone == null)
                        continue;

                    var sceneBone = FindByNamePathInScene(sceneRoot.transform, tmpBone);
                    if (sceneBone != null)
                        result[h] = sceneBone;
                }
                return result.Count > 0 ? result : null;
            }
            finally
            {
                // 원복 + 임시 에셋 정리
                try
                {
                    tmpImporter.animationType = origType;
                    tmpImporter.autoGenerateAvatarMappingIfUnspecified = origAuto;
                    tmpImporter.SaveAndReimport();
                }
                catch { /* ignore */ }
                AssetDatabase.DeleteAsset(tmpPath);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BoneConstraintMapper] 임시 복제 추출 중 예외: {e.Message}");
            return null;
        }
    }

    // ★ 새 헬퍼: 씬 오브젝트에서 원본 모델(또는 아바타) 에셋 경로를 얻는다.
    // 우선순위: Animator.avatar → Prefab 원본
    private string ResolveModelAssetPath(GameObject root)
    {
        if (root == null)
            return null;
        var animator = root.GetComponentInChildren<Animator>();

        // 1) Avatar가 연결되어 있으면 그 에셋 경로 사용 (대부분 FBX/sub-asset)
        if (animator != null && animator.avatar != null)
        {
            var avPath = AssetDatabase.GetAssetPath(animator.avatar);
            if (!string.IsNullOrEmpty(avPath))
                return avPath; // 보통 .fbx, 경우에 따라 .asset(Standalone Avatar)일 수도 있음
        }

        // 2) Prefab/FBX 원본
        UnityEngine.Object srcAsset = PrefabUtility.GetCorrespondingObjectFromSource(root) ?? root;
        var srcPath = AssetDatabase.GetAssetPath(srcAsset);
        if (!string.IsNullOrEmpty(srcPath))
            return srcPath;

        return null;
    }

    // ★ 새 폴백: Standalone Avatar(.asset) 또는 임포터에 접근할 수 없을 때,
    // Avatar의 HumanDescription에서 공식 본 매핑을 읽어 씬 본과 매칭한다.
    private Dictionary<HumanBodyBones, Transform> BuildMapFromAvatarDescription(Transform sceneRoot, Avatar avatar)
    {
        try
        {
            if (avatar == null || !avatar.isValid)
                return null;

            var map = new Dictionary<HumanBodyBones, Transform>();
            var desc = avatar.humanDescription; // HumanBone[] 포함

            // 이름 그대로 매칭(임포터가 확정한 본 이름). "추측"이 아니라 공식 매핑 이름을 사용하는 것.
            foreach (var hb in desc.human)
            {
                if (Enum.TryParse(hb.humanName, out HumanBodyBones h))
                {
                    var t = FindByNameInChildren(sceneRoot, hb.boneName); // 동일 본 이름 탐색
                    if (t != null)
                        map[h] = t;
                }
            }
            return map.Count > 0 ? map : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BoneConstraintMapper] Avatar humanDescription 매핑 실패: {e.Message}");
            return null;
        }
    }

    // ★ 보조: 자식 트리에서 정확 이름으로 탐색
    private Transform FindByNameInChildren(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindByNameInChildren(root.GetChild(i), name);
            if (r != null)
                return r;
        }
        return null;
    }

    private Texture2D MakeTex(int w, int h, Color c)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var fill = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
        var data = new Color32[w * h];
        for (int i = 0; i < data.Length; i++)
            data[i] = fill;
        t.SetPixels32(data);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }
}

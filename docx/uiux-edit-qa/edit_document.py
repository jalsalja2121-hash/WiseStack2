from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E

source = Path(r'C:\Users\admin22\Desktop\김동현2학년1학기_3\AR26\개발일지_11_20_컴퓨터공학과_김상엽_김동현.docx')
output = Path(r'C:\project\WiseStack2\docx\개발일지_11_20_컴퓨터공학과_김상엽_김동현_최종.docx')
ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
updates = {
    2: 'WiseStack',
    7: '2026.11.20',
    26: '추천 화면의 긴 상세 설명을 정리하여 추천 이유와 예상 수량을 쉽게 파악하도록 UI/UX 개선',
    27: '계산 근거를 포함해 설명을 10~15줄로 요약하고 펼치기·접기 사용성 개선',
    31: '김상엽·김동현 공동: 반복되는 전략별 수치를 추천안 중심으로 정리하고 선정 기준·계산 근거·가정과 한계로 구분',
    32: '공동 진행: 상세 설명을 소제목 포함 14줄로 구성하고, 펼치기·접기 시 문구·높이·스크롤 영역 갱신',
    36: '한 층 → 팔레트당 → 창고 전체 순서로 수량 산출 과정을 표시하고 높이·하중·한도를 함께 안내',
    37: '기존 검증에서 4개 해상도·예외 상태 검사 통과, 컴파일 오류 0건. 아래는 개선 전 화면',
    42: '화면 폭에 따른 줄바꿈과 기준 무게·창고 형상 가정, 현장 확인 사항 안내 필요',
    46: 'APK 재빌드 후 Android 기기에서 가독성·펼치기·접기·스크롤 검증',
}
with ZipFile(source) as src:
    root = E.fromstring(src.read('word/document.xml'))
    original = E.fromstring(src.read('word/document.xml'))
    paragraphs = root.xpath('//w:p', namespaces=ns)
    for index, text in updates.items():
        nodes = paragraphs[index].xpath('.//w:t', namespaces=ns)
        if index not in (2, 7):
            assert nodes[0].text == '\uf0b7'
            nodes = nodes[2:]
        # Keep all runs and their formatting; replace only their text payloads.
        nodes[0].text = text
        for node in nodes[1:]:
            node.text = ''
    # The XML structure and every non-text attribute must remain identical.
    def strip_text(tree):
        for node in tree.xpath('//w:t', namespaces=ns):
            node.text = ''
        return E.tostring(tree)
    edited = E.tostring(root, xml_declaration=True, encoding='UTF-8', standalone=True)
    assert strip_text(original) == strip_text(E.fromstring(edited))
    with ZipFile(output, 'w') as dst:
        for item in src.infolist():
            dst.writestr(item, edited if item.filename == 'word/document.xml' else src.read(item.filename))
with ZipFile(source) as a, ZipFile(output) as b:
    changed = [name for name in a.namelist() if a.read(name) != b.read(name)]
    assert changed == ['word/document.xml'], changed
print(output)
print('Verified: only text content changed; all formatting, images, and package parts preserved.')

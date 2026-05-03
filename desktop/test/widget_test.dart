import 'package:flutter_test/flutter_test.dart';

import 'package:bili_music_desktop/main.dart';

void main() {
  testWidgets('desktop app renders the sidebar brand', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(const BiliMusicDesktopApp());

    expect(find.text('BiliMusic'), findsOneWidget);
    expect(find.text('我的收藏'), findsOneWidget);
  });
}
